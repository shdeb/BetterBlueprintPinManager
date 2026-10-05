# Better Blueprint Pin Manager 
<small> by shdeb </small>

Do you find your self pinning multiple sub items just to track the one item which you actually want to make?

This mod wants to solve that problem, plus adds few quality of life features.

## Main features
* Shows nested dependencies for a pinned blueprint item.
* Shows " has / need " counts for the ingredients
* Resolves recipe correctly if parent/intermediate needs are satisfied. 
  <br/>it resolves the intermediate items which are present in the inventory.
* Scroll up/down on the pinned item (while in PDA screen) to adjust the pinned item's want count;
  <br/>this updates the needs for its ingredient dependencies, to reflect the higher/lower threshold to satisfy the requirement.
* Option to show only pending ingredients.
* Option to auto hide / peek the ingredients with a keybind.
* Option to show only raw ingredients.
* A new UI element, which, shows total raw ingredients needed across all the pinned blueprints.

## Installation
* Download the zip file, extract/copy the folder 
* and put it into the `...\Subnautica\BepInEx\plugins` directory.
* It should look like:<br/>
`...\Subnautica\BepInEx\plugins\BetterBlueprintPinManager\BetterBlueprintPinManager.dll`

## Requirements
* Subnautica BepInEx Pack
* Nautilus api
## Shout outs
Thanks to my sheer will to not be inconvenienced by semi-functional vanilla UI.

Thanks for checking out this mod.
