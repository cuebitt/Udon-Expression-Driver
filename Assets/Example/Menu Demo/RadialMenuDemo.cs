using UdonSharp;
using UnityEngine;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
#endif

namespace UdonExpressionDriver
{
    /// <summary>
    /// Demo driver for the UdonExpressionDriver example scene. Populates a Radial Menu with a
    /// fake expressions menu (buttons, toggles, submenus) and demonstrates menu navigation
    /// without driving a real prop's parameters. The puppet controls and the Hand Gestures wedge
    /// are stubs: they exist to show the wedges but never open the world-space controls (those
    /// are demonstrated separately in the Puppet Test group). Toggling Hand Gesture Emulation
    /// appends/removes the 'Hand Gestures' wedge at the top menu level, mirroring
    /// UEDFullController.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RadialMenuDemo : UEDMenuHost
    {
        private const int ControlButton = 0;
        private const int ControlToggle = 1;
        private const int ControlSubMenu = 2;
        private const int ControlTwoAxis = 3;
        private const int ControlFourAxis = 4;
        private const int ControlBack = 5;
        private const int ControlRadialPuppet = 6;

        private const int MaxMenuControls = 8;
        private const int MaxMenuStackDepth = 8;
        private const string HandGesturesControlName = "Hand Gestures";
        private const string GestureEmulationControlName = "Gesture Emulation";

        [Header("Menu View")]
        [Tooltip("Radial menu to populate with the demo expressions menu.")]
        [SerializeField] private RadialMenu menuView;
        [Tooltip("When on, a 'Hand Gestures' wedge is appended to the top menu level.")]
        [SerializeField] private bool enableHandGestureEmulation;

        // Embedded demo menu tree (no prop, no parameters are driven). The layout mirrors
        // UEDFullController: menuControlStart indexes the flat control arrays per menu.
        private int[] menuControlStart = { 0, 7, 10, 13, 15 };

        private int[] controlTypes =
        {
            ControlSubMenu, ControlSubMenu, ControlButton, ControlToggle,
            ControlRadialPuppet, ControlTwoAxis, ControlFourAxis,
            ControlToggle, ControlButton, ControlBack,
            ControlSubMenu, ControlToggle, ControlBack,
            ControlButton, ControlBack,
        };

        private string[] controlNames =
        {
            "Options", "Nested", "Button", "Toggle",
            "Radial Puppet", "2-Axis", "4-Axis",
            GestureEmulationControlName, "Button", "Back",
            "Deeper", "Toggle", "Back",
            "Deep Button", "Back",
        };

        private int[] controlSubmenuIndex =
        {
            1, 2, -1, -1, -1, -1, -1,
            -1, -1, -1,
            3, -1, -1,
            -1, -1,
        };

        // One icon per control, parallel to controlTypes/controlNames. Populated in the demo
        // scene with built-in engine textures so they work at runtime; null leaves a wedge
        // label-only.
        [SerializeField] private Texture2D[] controlIcons = new Texture2D[15];

        private int _currentMenu;
        private int[] _menuStack = new int[MaxMenuStackDepth];
        private int _menuStackDepth;

        private void Start()
        {
            _RefreshMenu();
        }

        public override void _OnControlPressed(int controlIndex)
        {
            var count = _GetCurrentMenuControlCount();
            if (controlIndex < 0 || controlIndex >= count) return;

            // The Hand Gestures wedge is a stub: it never opens the gesture menu.
            if (_IsHandGesturesSlot(controlIndex)) return;

            var flat = _DisplayFlat(controlIndex);
            var type = flat < controlTypes.Length ? controlTypes[flat] : -1;

            if (type == ControlToggle && _IsGestureEmulationControl(flat))
            {
                _ToggleHandGestureEmulation();
            }
            else if (type == ControlSubMenu)
            {
                var subMenu = flat < controlSubmenuIndex.Length ? controlSubmenuIndex[flat] : -1;
                if (subMenu >= 0) _OpenMenu(subMenu);
            }
            else if (type == ControlBack)
            {
                _Back();
            }
            // Buttons and puppet controls are stubs: nothing to drive, nothing to open.
        }

        public void _OpenMenu(int menuIndex)
        {
            if (menuIndex < 0 || menuIndex >= _GetMenuCount()) return;

            if (_menuStackDepth < MaxMenuStackDepth)
            {
                _menuStack[_menuStackDepth] = _currentMenu;
                _menuStackDepth++;
            }

            _currentMenu = menuIndex;
            _RefreshMenu();
        }

        public void _Back()
        {
            if (_menuStackDepth <= 0)
            {
                _currentMenu = 0;
                _RefreshMenu();
                return;
            }

            _menuStackDepth--;
            _currentMenu = _menuStack[_menuStackDepth];
            _RefreshMenu();
        }

        public void _SetHandGestureEmulation(bool enabled)
        {
            if (enableHandGestureEmulation == enabled) return;
            enableHandGestureEmulation = enabled;
            _RefreshMenu();
        }

        public bool _GetHandGestureEmulation()
        {
            return enableHandGestureEmulation;
        }

        public void _ToggleHandGestureEmulation()
        {
            _SetHandGestureEmulation(!enableHandGestureEmulation);
        }

        /// <summary>Interacting toggles the menu's visibility; reopening always starts at the top level.</summary>
        public override void Interact()
        {
            if (menuView == null) return;

            if (menuView.gameObject.activeSelf)
            {
                _ResetMenuNavigation();
                menuView._SetVisible(false);
            }
            else
            {
                _RefreshMenu();
                menuView._SetVisible(true);
            }
        }

        private void _RefreshMenu()
        {
            if (menuView == null) return;

            var count = _GetCurrentMenuControlCount();
            var names = new string[count];
            var icons = new Texture2D[count];
            for (var i = 0; i < count; i++)
            {
                names[i] = _GetControlName(i);
                icons[i] = _GetControlIcon(i);
            }

            menuView.SetContent(names, icons);
        }

        private bool _IsGestureEmulationControl(int flat)
        {
            return controlNames != null && flat < controlNames.Length && controlNames[flat] == GestureEmulationControlName;
        }

        private int _GetMenuCount()
        {
            return menuControlStart != null && menuControlStart.Length > 0 ? menuControlStart.Length - 1 : 0;
        }

        private int _GetCurrentMenuControlCount()
        {
            var count = _TopLevelBaseControlCount();
            if (_currentMenu == 0 && _IsHandGesturesVisible()) count++;
            return count;
        }

        // Capped at MaxMenuControls so the appended Hand Gestures wedge never overflows the
        // radial menu's segment array, matching UEDFullController's behavior.
        private int _TopLevelBaseControlCount()
        {
            var count = _NextControlStart() - _CurrentControlStart();
            if (count < 0) count = 0;
            if (count > MaxMenuControls) count = MaxMenuControls;
            if (_currentMenu == 0 && _IsHandGesturesVisible() && count > MaxMenuControls - 1) count = MaxMenuControls - 1;
            return count;
        }

        private bool _IsHandGesturesVisible()
        {
            return enableHandGestureEmulation;
        }

        private bool _IsHandGesturesSlot(int controlIndex)
        {
            return _currentMenu == 0 && _IsHandGesturesVisible() && controlIndex == _TopLevelBaseControlCount();
        }

        private string _GetControlName(int controlIndex)
        {
            if (_IsHandGesturesSlot(controlIndex)) return HandGesturesControlName;

            var flat = _DisplayFlat(controlIndex);
            if (controlNames == null || flat < 0 || flat >= controlNames.Length) return "";
            return controlNames[flat];
        }

        private Texture2D _GetControlIcon(int controlIndex)
        {
            // The appended Hand Gestures wedge reuses the Gesture Emulation control's icon.
            if (_IsHandGesturesSlot(controlIndex)) return _IconAt(7);

            return _IconAt(_DisplayFlat(controlIndex));
        }

        private Texture2D _IconAt(int flat)
        {
            if (controlIcons == null || flat < 0 || flat >= controlIcons.Length) return null;
            return controlIcons[flat];
        }

        private int _CurrentControlStart()
        {
            if (menuControlStart == null) return 0;
            return _currentMenu < menuControlStart.Length ? menuControlStart[_currentMenu] : 0;
        }

        private int _NextControlStart()
        {
            if (menuControlStart == null) return 0;
            var next = _currentMenu + 1;
            return next < menuControlStart.Length ? menuControlStart[next] : 0;
        }

        // Maps a displayed wedge index to its flat control index. A submenu's Back control is
        // always shown on the wedge closest to the top of the menu (display index 0), matching
        // VRChat's expressions menu; the remaining controls follow in their authored order.
        private int _DisplayFlat(int controlIndex)
        {
            var start = _CurrentControlStart();
            var end = _NextControlStart();
            if (end < start) end = start;

            var displayCount = end - start;
            if (displayCount > MaxMenuControls) displayCount = MaxMenuControls;

            var backFlat = -1;
            if (controlTypes != null)
            {
                for (var f = start; f < start + displayCount; f++)
                {
                    if (f >= controlTypes.Length) break;
                    if (controlTypes[f] == ControlBack) { backFlat = f; break; }
                }
            }

            if (backFlat < 0) return start + controlIndex;

            if (controlIndex == 0) return backFlat;

            var seen = 0;
            for (var f = start; f < start + displayCount; f++)
            {
                if (f == backFlat) continue;
                if (seen == controlIndex - 1) return f;
                seen++;
            }

            return start + controlIndex;
        }

        private void _ResetMenuNavigation()
        {
            _currentMenu = 0;
            _menuStackDepth = 0;
            _RefreshMenu();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-wire a sibling RadialMenu: dropping this component onto the demo menu's root
            // is enough, since the menu view reference and the menu's host reference are set here.
            var radialMenu = GetComponent<RadialMenu>();
            if (radialMenu == null) return;

            if (menuView != radialMenu) menuView = radialMenu;

            var radialSerialized = new SerializedObject(radialMenu);
            var fullController = radialSerialized.FindProperty("fullController");
            if (fullController != null && fullController.objectReferenceValue != this)
            {
                fullController.objectReferenceValue = this;
                radialSerialized.ApplyModifiedProperties();
            }
        }
#endif
    }
}
