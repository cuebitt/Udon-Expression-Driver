![Udon Expression Driver](/.github/media/Udon%20Expression%20Driver.png)

<br>
<div align="center" class="flex">

<img alt="GitHub License" src="https://img.shields.io/github/license/cuebitt/UdonExpressionDriver">

<img alt="GitHub Tag" src="https://img.shields.io/github/v/tag/cuebitt/UdonExpressionDriver?label=latest%20release">

<img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/cuebitt/UdonExpressionDriver">

<img alt="GitHub Actions Workflow Status" src="https://img.shields.io/github/actions/workflow/status/cuebitt/UdonExpressionDriver/release.yml">

</div>
<br>

Udon Expression Driver (UED) is a set of tools and runtime scripts for porting VRChat avatar props to Worlds without modifying them. Props that use Dynamics (Physbones and Contacts[^1]) keep working, and UED adds VRCFury-style Armature Link and Full Controller components, so a prop can stick to a chosen humanoid bone on the player wearing it. It also provides world-space emulated expressions menus driven by the prop's own data.

## Features

- [x] Automatic installer
- [x] Component-driven setup: `UEDArmatureLink` for positioning, `UEDFullController` for expressions and menu
- [x] World Armature Link: stick a prop to a player's humanoid bone at runtime
- [x] Emulated expressions menu driven from the prop's own data
- [x] Radial and axis puppet controls
- [x] Hand gesture emulation (drives the Animator's `GestureLeft`/`GestureRight` params)
- [x] Menu and controls gated to the prop's owner
- [x] Physbone/Contact event forwarders
  - [x] Physbone event forwarder script
  - [x] Contact event forwarder script

## Usage

<div align="center">
  
# ➡️ [Click here to add to VCC](https://cuebitt.github.io/vpm/) ⬅️

</div>

Add [my VPM repository](https://cuebitt.github.io/vpm/) to VCC or ALCOM and install `UdonExpressionDriver` into your World project.

Setup is component-driven. Add a `UEDArmatureLink` to a prop prefab and pick the bone it should stick to. For expressions, add a `UEDFullController` and point it at the prop's expressions menu and parameters. The controller exposes the prop's menu in front of the player's head, including puppet controls for radial and axis parameters and a hand gesture panel. Only the prop's owner can open the menu or drive its controls, mirroring how an avatar expressions menu behaves.

If the prop already uses VRCFury, `UEDArmatureLink` and `UEDFullController` import their config automatically, so there is no need to rebuild the setup by hand.

Physbone and contact forwarders, and the puppet and gesture controls, are added to the prop's children automatically when you enter play mode or build, then removed again afterwards, so your prefab is never changed.

## Troubleshooting

UED is still early in development, so expect rough edges.

The first time you import the package, Unity may warn about missing scripts and suggest entering safe mode. That is expected. Exit safe mode and the warning clears. It only happens once per project.

## Attribution

UED downloads VRChat's Avatars SDK (`VRCSDK3A.dll`) and modifies it. Everything is stripped out except `VRCExpressionsMenu`, `VRCExpressionParameters`, and what they depend on.

UED itself is released under the MIT license.

[^1]: Constraints are imported as-is.
