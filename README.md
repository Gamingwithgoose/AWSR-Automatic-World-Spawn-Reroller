# Automatic World Spawn Reroll

**Automatic World Spawn Reroll**, or **AWSR**, is a small preloader patcher for *Monsterpatch* that prevents overworld routes from staying empty after every visible mon has been shooed away.

Normally, overworld mons are only rolled again after changing locations. AWSR allows the current area to repopulate without forcing the player to leave the route and return.

## What AWSR Does

When the final overworld mon in the current area is shooed away, AWSR immediately performs a normal overworld spawn roll.

If that roll produces no mons, AWSR begins counting the player's movement. After 10 ground steps, it performs another normal spawn roll.

If the route is still empty, AWSR waits another 10 steps and tries again. This continues until at least one overworld mon successfully appears.

AWSR does not guarantee a spawn and does not change the game's normal spawn percentages. It simply gives the current area another chance to populate.

### Current Behavior

- Shooing the final overworld mon triggers an immediate spawn reroll.
- A failed roll is retried after 10 ground steps.
- Additional failed rolls are retried every 10 steps.
- Recovery stops as soon as at least one overworld mon appears.
- Walking and running count toward the step total.
- Broom movement does not count.
- Random battles do not trigger a reroll.
- Changing locations cancels the recovery process because the game handles the new location normally.
- The original overworld spawn odds and rarity selection remain unchanged.

## Installation

1. Close *Monsterpatch* completely.
2. Remove any older plugin version of AWSR from:

   ```text
   Monsterpatch\BepInEx\plugins\AutomaticWorldSpawnReroll\
   ```

3. Extract the AWSR release ZIP into the main *Monsterpatch* folder.

4. Confirm that the patcher DLL is located here:

   ```text
   Monsterpatch\BepInEx\patchers\AutomaticWorldSpawnReroll\
       AutomaticWorldSpawnReroll.Patcher.dll
   ```

5. Delete the BepInEx cache folder if it exists:

   ```text
   Monsterpatch\BepInEx\cache\
   ```

6. Launch *Monsterpatch* normally.

Deleting the cache is important when installing or updating the patcher. BepInEx may otherwise reuse an older cached copy of the game's patched assembly.

## Usage

AWSR works automatically. There are no menus, hotkeys, or commands.

To use it:

1. Enter an area containing visible overworld mons.
2. Shoo away each mon in the area.
3. When the final mon is removed, AWSR immediately performs a normal spawn roll.
4. If no mon appears, walk 10 ground steps.
5. AWSR performs another roll after the tenth step.
6. Continue walking if the route remains empty. AWSR retries every 10 steps until a spawn succeeds.

## Testing

A simple test can be performed on any route with visible overworld mons:

1. Enter the route and note the visible mons.
2. Shoo every visible mon.
3. Watch for the immediate reroll.
4. If the route stays empty, walk exactly 10 ground steps.
5. Confirm that another spawn roll occurs.
6. If the second roll is also empty, continue for another 10 steps.

The patch is working correctly if the route can repopulate without leaving and re-entering the area.

## Troubleshooting

### Nothing happens after shooing the final mon

Confirm that the DLL is inside:

```text
BepInEx\patchers\AutomaticWorldSpawnReroll\
```

It will not work from the normal `BepInEx\plugins` folder.

Also delete:

```text
BepInEx\cache\
```

Then relaunch the game.

### AWSR does not appear in the log

Check that BepInEx is loading correctly and that the file is named:

```text
AutomaticWorldSpawnReroll.Patcher.dll
```

Make sure the release ZIP was extracted into the game root rather than into an extra nested folder.

### The route is still empty after 10 steps

AWSR performs a normal spawn roll; it does not force a mon to appear. Walk another 10 steps to trigger another attempt.

Only actual ground movement counts. Broom travel is intentionally excluded.

### The game stops loading after an update

A *Monsterpatch* update may replace or significantly change `Assembly-CSharp.dll`. Because AWSR injects code into that assembly before the game loads, a major game update may require a compatible AWSR build.

Remove the patcher temporarily and review `BepInEx\LogOutput.log` for the failed patch target.

## Uninstallation

1. Close the game.
2. Delete:

   ```text
   Monsterpatch\BepInEx\patchers\AutomaticWorldSpawnReroll\
   ```

3. Delete:

   ```text
   Monsterpatch\BepInEx\cache\
   ```

4. Launch the game normally.

AWSR does not modify save files, so no save cleanup is required.

## Save Safety

AWSR does not add custom data to the player's save file.

Removing the patcher returns overworld spawning to the game's normal behavior.

As with any mod, keeping a backup of important saves is still recommended.

## Credits

Created for the *Monsterpatch* community by **Goose**.

AWSR was designed to preserve the game's original overworld spawn rolls while removing the need to repeatedly leave and re-enter a route after all visible mons have been shooed.

## Disclaimer

AWSR’s original source code is licensed under the MIT License. Monsterpatch, BepInEx, Mono.Cecil, and all other third-party software remain the property of their respective owners and are not covered by this license.
Automatic World Spawn Reroll is an unofficial community modification and is not affiliated with or endorsed by the developers or publishers of *Monsterpatch*.
