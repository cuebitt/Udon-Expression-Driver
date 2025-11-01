![Udon Expression Driver](/.github/media/Udon%20Expression%20Driver.png)

<br>
<div align="center" class="flex">

<img alt="GitHub License" src="https://img.shields.io/github/license/cuebitt/UdonExpressionDriver">

<img alt="GitHub Tag" src="https://img.shields.io/github/v/tag/cuebitt/UdonExpressionDriver?label=latest%20release">

<img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/cuebitt/UdonExpressionDriver">

<img alt="GitHub Actions Workflow Status" src="https://img.shields.io/github/actions/workflow/status/cuebitt/UdonExpressionDriver/release.yml">

</div>
<br>

Udon Expression Driver is a set of tools and runtime scripts used to non-destructively port VRChat avatar props with Dynamics (Physbones and Contacts[^1]) to Worlds.

## Features

- [x] Automatic installer
- [x] `VRCExpressionsMenu` + `VRCExpressionParameter` -> JSON extractor
- [x] JSON -> Driver UdonSharpBehaviour
  - [ ] Custom inspector for behaviour class
- [ ] Avatars-like menu prefab generator
- [ ] Physbone/Contact event forwarder tool
  - [ ] Runtime Physbone event forwarder script
  - [ ] Runtime Contact event forwarder script

## Usage

<div align="center">
  
# ➡️ [Click here to add to VCC](https://cuebitt.github.io/vpm/) ⬅️

</div>

Add [my VPM repository](https://cuebitt.github.io/vpm/) to VCC/ALCOM and install the `UdonExpressionDriver` package to your World project.

Detailed steps will be included here once more of the above features are implemented. You can find the work-in-progress editor menu by selecting Tools > Udon Expression Driver.

## Troubleshooting

Udon Expression Driver is in active early development, so you may run into issues when using it yourself.

When importing Udon Expression Driver to a project for the first time, Unity may warn you about missing scripts and suggest you enter safe mode. This is expected and will be fixed when you exit safe mode. This should only occur once after importing Udon Expression Driver to a project.

## Attribution

Udon Expression Driver downloads and modifies VRChat's Avatars SDK (`VRCSDK3A.dll`). The following changes are made:

- All symbols other than `VRCExpressionsMenu`, `VRCExpressionParameters`, and their dependencies are stripped out.

Udon Expression Driver itself is released under the MIT license.

[^1]: Constraints are imported as-is.
