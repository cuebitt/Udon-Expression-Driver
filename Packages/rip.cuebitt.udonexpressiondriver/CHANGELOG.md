# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Refactoring only; no feature or API changes planned.

## [1.0.0] - 2026-08-13

### Added

- Component-driven setup replacing the codegen pipeline: `UEDArmatureLink` for positioning and `UEDFullController` for expressions and menus, with all configuration embedded on the component.
- VRCFury config import, so a prop already set up with VRCFury Armature Links and Full Controllers transfers into UED without rebuilding by hand.
- World-space emulated expressions menu driven from the prop's own expressions data.
- Radial puppet control (single float) and two- and four-axis puppet controls (vector floats), shown in front of the player's head.
- Hand gesture emulation: a Hand Gestures wedge in the top menu level that drives the Animator's `GestureLeft` and `GestureRight` parameters from the standard eight gestures per hand.
- Menu, puppet, and hand-gesture controls gated to the prop's owner, matching how an avatar expressions menu behaves.
- Physbone and contact event forwarders, added to the prop automatically at build time and play time and removed afterwards so the authored prop is never modified.

### Changed

- The previous UI Toolkit editor window, T4 driver generator, and JSON to script workflow were removed.
- `UdonExposureDumper` now writes the Udon whitelist as JSON instead of a text dump.

### Fixed

- Hand Gesture menu prefab path and FingerPoint icon.

[Unreleased]: https://github.com/cuebitt/UdonExpressionDriver/compare/1.0.0...HEAD
[1.0.0]: https://github.com/cuebitt/UdonExpressionDriver/releases/tag/1.0.0
