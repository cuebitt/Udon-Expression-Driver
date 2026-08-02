![Udon Expression Driver](/.github/media/Udon%20Expression%20Driver.png)

<br>
<div align="center" class="flex">

<img alt="GitHub License" src="https://img.shields.io/github/license/cuebitt/UdonExpressionDriver">

<img alt="GitHub Tag" src="https://img.shields.io/github/v/tag/cuebitt/UdonExpressionDriver?label=latest%20release">

<img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/cuebitt/UdonExpressionDriver">

<img alt="GitHub Actions Workflow Status" src="https://img.shields.io/github/actions/workflow/status/cuebitt/UdonExpressionDriver/release.yml">

</div>
<br>

Udon Expression Driver is a set of tools and runtime scripts used to non-destructively port VRChat avatar props with Dynamics (Physbones and Contacts[^1]) to Worlds. It adds VRCFury-style Armature Links (props that stick to a player's selected humanoid bone) and world-space emulated expressions menus, with fully functional PhysBones and Contacts.

## Features

- [x] Automatic installer
- [ ] Component-driven setup (`UEDArmatureLink` positioning + `UEDFullController` expressions/menu) — in progress
- [ ] World Armature Link: stick a prop to a player's humanoid bone at runtime
- [ ] Emulated expressions menu (radial menu) driven from the prop's own data
- [ ] Physbone/Contact event forwarder tool
  - [ ] Runtime Physbone event forwarder script
  - [ ] Runtime Contact event forwarder script

> The original JSON extractor → generated-driver-script pipeline was removed in the v2 cleanup.

## Usage

<div align="center">
  
# ➡️ [Click here to add to VCC](https://cuebitt.github.io/vpm/) ⬅️

</div>

Add [my VPM repository](https://cuebitt.github.io/vpm/) to VCC/ALCOM and install the `UdonExpressionDriver` package to your World project.

Detailed steps will be included here once more of the above features are implemented. Setup is component-driven: add the UED components to a prop prefab and drop it in your scene.

## Troubleshooting

Udon Expression Driver is in active early development, so you may run into issues when using it yourself.

When importing Udon Expression Driver to a project for the first time, Unity may warn you about missing scripts and suggest you enter safe mode. This is expected and will be fixed when you exit safe mode. This should only occur once after importing Udon Expression Driver to a project.

## Attribution

Udon Expression Driver downloads and modifies VRChat's Avatars SDK (`VRCSDK3A.dll`). The following changes are made:

- All symbols other than `VRCExpressionsMenu`, `VRCExpressionParameters`, and their dependencies are stripped out.

Udon Expression Driver itself is released under the MIT license.

[^1]: Constraints are imported as-is.
