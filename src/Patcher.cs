using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Goose.Monsterpatch.AWSR
{
    /// <summary>
    /// BepInEx 5 preloader patcher. It injects direct calls into Assembly-CSharp
    /// before Monsterpatch loads. No Harmony or plugin DLL is used.
    /// </summary>
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs
        {
            get { return new[] { "Assembly-CSharp.dll" }; }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            if (assembly == null || assembly.MainModule == null)
            {
                throw new ArgumentNullException("assembly");
            }

            ModuleDefinition module = assembly.MainModule;

            InjectObjectHookAtStart(module, "GameScript", "Start", 0,
                typeof(AWSRRuntime).GetMethod("OnGameStart", BindingFlags.Public | BindingFlags.Static));

            InjectObjectHookAtEnd(module, "GameScript", "ShooAway", 0,
                typeof(AWSRRuntime).GetMethod("OnShooAway", BindingFlags.Public | BindingFlags.Static));

            InjectObjectHookAtEnd(module, "PlayerController", "NewTileCheck", 1,
                typeof(AWSRRuntime).GetMethod("OnTileStep", BindingFlags.Public | BindingFlags.Static));

            InjectSetLocationHook(module);

            Console.WriteLine("[AWSR] Assembly-CSharp prepatch applied successfully (v1.1.0).");
        }

        private static void InjectObjectHookAtStart(
            ModuleDefinition module,
            string typeName,
            string methodName,
            int parameterCount,
            MethodInfo runtimeMethod)
        {
            MethodDefinition target = FindInstanceMethod(module, typeName, methodName, parameterCount);
            if (HasRuntimeCall(target, runtimeMethod))
            {
                return;
            }

            MethodReference imported = module.ImportReference(runtimeMethod);
            ILProcessor il = target.Body.GetILProcessor();
            Instruction first = target.Body.Instructions.First();

            target.Body.MaxStackSize = Math.Max(target.Body.MaxStackSize, 2);
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, imported));
        }

        private static void InjectObjectHookAtEnd(
            ModuleDefinition module,
            string typeName,
            string methodName,
            int parameterCount,
            MethodInfo runtimeMethod)
        {
            MethodDefinition target = FindInstanceMethod(module, typeName, methodName, parameterCount);
            if (HasRuntimeCall(target, runtimeMethod))
            {
                return;
            }

            MethodReference imported = module.ImportReference(runtimeMethod);
            ILProcessor il = target.Body.GetILProcessor();
            Instruction[] returns = target.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Ret)
                .ToArray();

            if (returns.Length == 0)
            {
                throw new InvalidOperationException(typeName + "." + methodName + " has no return instruction.");
            }

            target.Body.MaxStackSize = Math.Max(target.Body.MaxStackSize, 2);
            foreach (Instruction ret in returns)
            {
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(OpCodes.Call, imported));
            }
        }

        private static void InjectSetLocationHook(ModuleDefinition module)
        {
            TypeDefinition type = FindType(module, "GameScript");
            MethodDefinition target = type.Methods.FirstOrDefault(m =>
                m.Name == "SetLocation" &&
                !m.IsStatic &&
                m.Parameters.Count == 2 &&
                m.Parameters[0].ParameterType.FullName == "System.String" &&
                m.Parameters[1].ParameterType.FullName == "System.Boolean");

            if (target == null)
            {
                throw new MissingMethodException("GameScript", "SetLocation(string, bool)");
            }

            MethodInfo runtimeMethod = typeof(AWSRRuntime).GetMethod(
                "OnSetLocation", BindingFlags.Public | BindingFlags.Static);

            if (HasRuntimeCall(target, runtimeMethod))
            {
                return;
            }

            MethodReference imported = module.ImportReference(runtimeMethod);
            ILProcessor il = target.Body.GetILProcessor();
            Instruction first = target.Body.Instructions.First();

            target.Body.MaxStackSize = Math.Max(target.Body.MaxStackSize, 3);
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, imported));
        }

        private static MethodDefinition FindInstanceMethod(
            ModuleDefinition module,
            string typeName,
            string methodName,
            int parameterCount)
        {
            TypeDefinition type = FindType(module, typeName);
            MethodDefinition method = type.Methods.FirstOrDefault(m =>
                m.Name == methodName &&
                !m.IsStatic &&
                m.Parameters.Count == parameterCount);

            if (method == null)
            {
                throw new MissingMethodException(typeName, methodName);
            }

            if (!method.HasBody)
            {
                throw new InvalidOperationException(typeName + "." + methodName + " does not have an IL body.");
            }

            return method;
        }

        private static TypeDefinition FindType(ModuleDefinition module, string typeName)
        {
            TypeDefinition type = module.Types.FirstOrDefault(t => t.Name == typeName);
            if (type == null)
            {
                throw new TypeLoadException("AWSR could not find type '" + typeName + "' in Assembly-CSharp.");
            }

            return type;
        }

        private static bool HasRuntimeCall(MethodDefinition target, MethodInfo runtimeMethod)
        {
            if (target == null || runtimeMethod == null || !target.HasBody)
            {
                return false;
            }

            string declaringType = runtimeMethod.DeclaringType.FullName;
            string methodName = runtimeMethod.Name;

            foreach (Instruction instruction in target.Body.Instructions)
            {
                MethodReference called = instruction.Operand as MethodReference;
                if (called != null &&
                    called.Name == methodName &&
                    called.DeclaringType.FullName == declaringType)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Runtime code called by the injected Assembly-CSharp hooks.
    /// It deliberately uses reflection so the patcher can be built without
    /// bundling or locating Monsterpatch/Unity game assemblies.
    /// </summary>
    public static class AWSRRuntime
    {
        private const int RetryStepThreshold = 10;
        private const string Version = "1.1.0";

        private static bool _recoveryActive;
        private static bool _spawnAttemptInProgress;
        private static int _stepsSinceFailedRoll;
        private static string _recoveryLocation;
        private static int _stateGeneration;
        private static bool _runtimeReadyLogged;

        public static void OnGameStart(object gameScript)
        {
            InvalidateAndClearState();

            if (!_runtimeReadyLogged)
            {
                _runtimeReadyLogged = true;
                Log("Runtime hooks active (v" + Version + "). Retry threshold: " + RetryStepThreshold + " steps.");
            }
        }

        public static void OnShooAway(object gameScript)
        {
            if (gameScript == null)
            {
                return;
            }

            _stateGeneration++;
            int generation = _stateGeneration;

            _recoveryActive = false;
            _spawnAttemptInProgress = false;
            _stepsSinceFailedRoll = 0;
            _recoveryLocation = GetCurrentLocation(gameScript);

            if (string.IsNullOrEmpty(_recoveryLocation))
            {
                CompleteState(generation);
                return;
            }

            IEnumerator routine = CheckAfterShooAway(gameScript, _recoveryLocation, generation);
            if (!TryStartCoroutine(gameScript, routine))
            {
                Log("ERROR: Could not start the post-shoo check coroutine.");
                CompleteState(generation);
            }
        }

        public static void OnTileStep(object playerController)
        {
            if (!_recoveryActive || _spawnAttemptInProgress || playerController == null)
            {
                return;
            }

            if (GetStaticBoolean(playerController.GetType(), "ridingBroom"))
            {
                return;
            }

            object gameScript = GetFieldValue(playerController, "gameScript");
            if (!IsSameLocation(gameScript, _recoveryLocation))
            {
                InvalidateAndClearState();
                return;
            }

            if (CountActiveRouteMons(gameScript) > 0)
            {
                InvalidateAndClearState();
                return;
            }

            _stepsSinceFailedRoll++;
            if (_stepsSinceFailedRoll < RetryStepThreshold)
            {
                return;
            }

            _stepsSinceFailedRoll = 0;
            int generation = _stateGeneration;
            IEnumerator routine = RunNativeSpawnAttempt(
                gameScript,
                _recoveryLocation,
                generation,
                RetryStepThreshold + "-step retry");

            if (!TryStartCoroutine(gameScript, routine))
            {
                Log("ERROR: Could not start the " + RetryStepThreshold + "-step retry coroutine.");
                CompleteState(generation);
            }
        }

        public static void OnSetLocation(object gameScript, string newLocation)
        {
            if (gameScript == null)
            {
                return;
            }

            string currentLocation = GetCurrentLocation(gameScript);
            if (!string.Equals(currentLocation, newLocation, StringComparison.Ordinal))
            {
                InvalidateAndClearState();
            }
        }

        private static IEnumerator CheckAfterShooAway(
            object gameScript,
            string expectedLocation,
            int generation)
        {
            // Unity Destroy() is deferred. One frame allows the shooed child
            // to be removed from GameScript._OverworldMons.
            yield return null;

            if (!IsCurrentState(generation) || !IsSameLocation(gameScript, expectedLocation))
            {
                yield break;
            }

            int remaining = CountActiveRouteMons(gameScript);
            if (remaining > 0)
            {
                CompleteState(generation);
                yield break;
            }

            if (!LocationHasSpawnZones(gameScript, expectedLocation))
            {
                CompleteState(generation);
                yield break;
            }

            Log("Last overworld mon shooed in '" + expectedLocation + "'. Running immediate spawn reroll.");
            yield return RunNativeSpawnAttempt(
                gameScript,
                expectedLocation,
                generation,
                "last mon shooed");
        }

        private static IEnumerator RunNativeSpawnAttempt(
            object gameScript,
            string expectedLocation,
            int generation,
            string reason)
        {
            if (!IsCurrentState(generation) || _spawnAttemptInProgress)
            {
                yield break;
            }

            if (!IsSameLocation(gameScript, expectedLocation))
            {
                CompleteState(generation);
                yield break;
            }

            object spawnManager = GetFieldValue(gameScript, "wildMonSpawnManger");
            if (spawnManager == null)
            {
                Log("ERROR: GameScript.wildMonSpawnManger was null.");
                CompleteState(generation);
                yield break;
            }

            _spawnAttemptInProgress = true;
            _recoveryActive = false;
            _stepsSinceFailedRoll = 0;

            IEnumerator nativeRoll;
            try
            {
                nativeRoll = InvokeIEnumerator(
                    spawnManager,
                    "RollForWildMonsInAZone2",
                    new object[] { expectedLocation });
            }
            catch (Exception ex)
            {
                Log("ERROR: Could not create native spawn roll: " + Unwrap(ex));
                CompleteState(generation);
                yield break;
            }

            if (nativeRoll == null)
            {
                Log("ERROR: RollForWildMonsInAZone2 returned null.");
                CompleteState(generation);
                yield break;
            }

            Log("Running native spawn roll in '" + expectedLocation + "' (" + reason + ").");

            while (true)
            {
                if (!IsCurrentState(generation) || !IsSameLocation(gameScript, expectedLocation))
                {
                    DisposeEnumerator(nativeRoll);
                    yield break;
                }

                bool hasNext;
                object current = null;

                try
                {
                    hasNext = nativeRoll.MoveNext();
                    if (hasNext)
                    {
                        current = nativeRoll.Current;
                    }
                }
                catch (Exception ex)
                {
                    Log("ERROR: Native spawn roll failed: " + Unwrap(ex));
                    DisposeEnumerator(nativeRoll);
                    CompleteState(generation);
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return current;
            }

            DisposeEnumerator(nativeRoll);

            if (!IsCurrentState(generation) || !IsSameLocation(gameScript, expectedLocation))
            {
                yield break;
            }

            _spawnAttemptInProgress = false;

            int spawned = CountActiveRouteMons(gameScript);
            if (spawned > 0)
            {
                Log("Spawn roll succeeded with " + spawned + " overworld mon(s). Recovery complete.");
                CompleteState(generation);
                yield break;
            }

            _recoveryActive = true;
            _stepsSinceFailedRoll = 0;
            Log("Spawn roll produced 0 mons. AWSR will retry after " + RetryStepThreshold + " ground steps.");
        }

        private static int CountActiveRouteMons(object gameScript)
        {
            if (gameScript == null)
            {
                return 0;
            }

            object parent = GetFieldValue(gameScript, "_OverworldMons");
            if (parent == null)
            {
                return 0;
            }

            int childCount = GetIntegerProperty(parent, "childCount");
            int count = 0;

            for (int i = 0; i < childCount; i++)
            {
                object child = InvokeMethod(parent, "GetChild", new object[] { i });
                if (child == null)
                {
                    continue;
                }

                string name = GetStringProperty(child, "name");
                if (!string.IsNullOrEmpty(name) &&
                    name.IndexOf("starter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool LocationHasSpawnZones(object gameScript, string location)
        {
            if (gameScript == null || string.IsNullOrEmpty(location))
            {
                return false;
            }

            object spawnManager = GetFieldValue(gameScript, "wildMonSpawnManger");
            if (spawnManager == null)
            {
                return false;
            }

            object managerTransform = GetPropertyValue(spawnManager, "transform");
            if (managerTransform == null)
            {
                return false;
            }

            object locationRoot = InvokeMethod(managerTransform, "Find", new object[] { location });
            if (locationRoot == null)
            {
                return false;
            }

            int childCount = GetIntegerProperty(locationRoot, "childCount");
            for (int i = 0; i < childCount; i++)
            {
                object child = InvokeMethod(locationRoot, "GetChild", new object[] { i });
                string childName = GetStringProperty(child, "name");
                if (childName == "spawnZone" || childName == "spawnZoneWater")
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCurrentLocation(object gameScript)
        {
            object value = GetFieldValue(gameScript, "curLocation");
            return value as string;
        }

        private static bool IsSameLocation(object gameScript, string expectedLocation)
        {
            return gameScript != null &&
                   !string.IsNullOrEmpty(expectedLocation) &&
                   string.Equals(GetCurrentLocation(gameScript), expectedLocation, StringComparison.Ordinal);
        }

        private static bool IsCurrentState(int generation)
        {
            return generation == _stateGeneration;
        }

        private static void CompleteState(int generation)
        {
            if (IsCurrentState(generation))
            {
                InvalidateAndClearState();
            }
        }

        private static void InvalidateAndClearState()
        {
            _stateGeneration++;
            _recoveryActive = false;
            _spawnAttemptInProgress = false;
            _stepsSinceFailedRoll = 0;
            _recoveryLocation = null;
        }

        private static bool TryStartCoroutine(object monoBehaviour, IEnumerator routine)
        {
            if (monoBehaviour == null || routine == null)
            {
                return false;
            }

            try
            {
                MethodInfo method = monoBehaviour.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "StartCoroutine")
                        {
                            return false;
                        }

                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 1 &&
                               parameters[0].ParameterType.FullName == "System.Collections.IEnumerator";
                    });

                if (method == null)
                {
                    return false;
                }

                method.Invoke(monoBehaviour, new object[] { routine });
                return true;
            }
            catch (Exception ex)
            {
                Log("ERROR: StartCoroutine reflection call failed: " + Unwrap(ex));
                return false;
            }
        }

        private static IEnumerator InvokeIEnumerator(object target, string methodName, object[] args)
        {
            object result = InvokeMethod(target, methodName, args);
            return result as IEnumerator;
        }

        private static object InvokeMethod(object target, string methodName, object[] args)
        {
            if (target == null)
            {
                return null;
            }

            int parameterCount = args == null ? 0 : args.Length;
            MethodInfo method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == parameterCount);

            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            return method.Invoke(target, args);
        }

        private static object GetFieldValue(object target, string fieldName)
        {
            if (target == null)
            {
                return null;
            }

            FieldInfo field = FindField(target.GetType(), fieldName);
            return field == null ? null : field.GetValue(target);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                if (field != null)
                {
                    return field;
                }

                current = current.BaseType;
            }

            return null;
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return property == null ? null : property.GetValue(target, null);
        }

        private static int GetIntegerProperty(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            if (value == null)
            {
                return 0;
            }

            return Convert.ToInt32(value);
        }

        private static string GetStringProperty(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value as string;
        }

        private static bool GetStaticBoolean(Type type, string fieldName)
        {
            FieldInfo field = FindField(type, fieldName);
            if (field == null)
            {
                return false;
            }

            object value = field.GetValue(null);
            return value is bool && (bool)value;
        }

        private static void DisposeEnumerator(IEnumerator enumerator)
        {
            IDisposable disposable = enumerator as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }

        private static string Unwrap(Exception exception)
        {
            TargetInvocationException tie = exception as TargetInvocationException;
            if (tie != null && tie.InnerException != null)
            {
                return tie.InnerException.ToString();
            }

            return exception.ToString();
        }

        private static void Log(string message)
        {
            Console.WriteLine("[AWSR] " + message);
        }
    }
}
