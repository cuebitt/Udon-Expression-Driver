using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Drives a prop's Animator from expression parameters and exposes the prop's
    /// expressions menu (controls) to a world-space menu view.
    /// All configuration is embedded on the component, with no runtime ScriptableObject.
    /// Synced parameters are owner-written; all clients apply them to the Animator.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UEDFullController : UEDBehaviour
    {
        private const int ParamTypeFloat = 0;
        private const int ParamTypeInt = 1;
        private const int ParamTypeBool = 2;

        private const int ControlButton = 0;
        private const int ControlToggle = 1;
        private const int ControlSubMenu = 2;
        private const int ControlTwoAxis = 3;
        private const int ControlFourAxis = 4;
        private const int ControlBack = 5;
        private const int ControlRadialPuppet = 6;
        private const int ControlHandGestures = 7;

        private const string GestureLeftName = "GestureLeft";
        private const string GestureRightName = "GestureRight";
        private const string HandGesturesControlName = "Hand Gestures";

        private const int MaxMenuStackDepth = 8;
        private const int MaxMenuControls = 8;
        private const float ToggleEpsilon = 0.001f;

        [Header("Animator")]
        [Tooltip("Animator on the prop whose parameters this controller drives.")]
        [SerializeField] private Animator animator;

        [Header("Parameters")]
        [Tooltip("Animator parameter names, one per entry.")]
        [SerializeField] private string[] paramNames;
        [Tooltip("Parameter types: 0 = float, 1 = int, 2 = bool.")]
        [SerializeField] private int[] paramTypes;
        [Tooltip("Default value for each parameter.")]
        [SerializeField] private float[] paramDefaults;
        [Tooltip("Whether each parameter is network-synced. Unsynced parameters are local to the owner.")]
        [SerializeField] private bool[] paramSynced;

        [Header("Menu")]
        [Tooltip("Start index into the control arrays for each menu; length = menu count + 1.")]
        [SerializeField] private int[] menuControlStart;
        [Tooltip("Control types: 0 = button, 1 = toggle, 2 = submenu, 3 = two-axis, 4 = four-axis, 5 = back.")]
        [SerializeField] private int[] controlTypes;
        [SerializeField] private string[] controlNames;
        [SerializeField] private Texture2D[] controlIcons;
        [Tooltip("Parameter index each control operates on, -1 for none.")]
        [SerializeField] private int[] controlParamIndex;
        [Tooltip("Value a button sets or a toggle activates.")]
        [SerializeField] private float[] controlValues;
        [Tooltip("Submenu index for submenu controls, -1 for none.")]
        [SerializeField] private int[] controlSubmenuIndex;
        [Tooltip("Start index into controlSubParams for each control's puppet sub-parameters, -1 for non-puppets.")]
        [SerializeField] private int[] controlSubParamStart;
        [Tooltip("Puppet sub-parameter parameter indices (flat), in the order the puppet emits them.")]
        [SerializeField] private int[] controlSubParams;

        [Tooltip("Radial menu view to populate from this controller's current menu level.")]
        [SerializeField] private RadialMenu menuView;
        [Tooltip("Whether interacting with this prop toggles the menu visibility.")]
        [SerializeField] private bool interactTogglesMenu = true;

        [Tooltip("World-space radial puppet control shown when a radial puppet menu item is opened.")]
        [SerializeField] private RadialPuppet radialPuppet;
        [Tooltip("World-space axis puppet control shown when a two- or four-axis puppet menu item is opened.")]
        [SerializeField] private AxisPuppet axisPuppet;
        [Tooltip("When on, a 'Hand Gestures' wedge is appended to the top menu level if the Animator uses GestureLeft or GestureRight.")]
        [SerializeField] private bool enableHandGestureEmulation;
        [Tooltip("World-space hand gesture menu shown when the Hand Gestures menu item is opened.")]
        [SerializeField] private HandGestureMenu handGestures;

        [SerializeField, HideInInspector] private RuntimeAnimatorController importedAnimatorController;
        [SerializeField, HideInInspector] private string importedMenuGuid;
        [SerializeField, HideInInspector] private string importedParametersGuid;
        [SerializeField, HideInInspector] private string generatedControllerGuid;
        [SerializeField, HideInInspector] private string generatedSourceGuid;
        [SerializeField, HideInInspector] private bool autoAddedAnimator;
        [SerializeField, HideInInspector] private string autoAddedHandGestureParams;

        [UdonSynced] private float[] _syncedValues = new float[0];
        private float[] _localValues = new float[0];
        private int[] _syncedSlot = new int[0];
        private int[] _localSlot = new int[0];
        private int[] _paramHashes = new int[0];

        private int _currentMenu;
        private int[] _menuStack = new int[MaxMenuStackDepth];
        private int _menuStackDepth;
        private int _activePuppetFlat = -1;
        private bool _activeHandGestures;

        private int _gestureLeftIndex = -1;
        private int _gestureRightIndex = -1;
        private bool _animatorUsesHandGestures;

        private void Start()
        {
            _EnsureArrays();
            _InitParamSlots();

            _ApplyAllToAnimator();
            _RefreshMenuView();

            _InitHandGestures();

            // Start with a clean slate (no stale UI for late joiners or freshly worn props).
            _CloseAllMenus();
        }

        // Resolves the GestureLeft/GestureRight param indices and whether the Animator actually
        // uses them, so the Hand Gestures wedge only appears when both the toggle and the Animator agree.
        private void _InitHandGestures()
        {
            _gestureLeftIndex = _FindParamIndex(GestureLeftName);
            _gestureRightIndex = _FindParamIndex(GestureRightName);
            _animatorUsesHandGestures = _gestureLeftIndex >= 0 || _gestureRightIndex >= 0 || _GestureParamInAnimator();

            if (handGestures != null) handGestures.gameObject.SetActive(false);
        }

        private int _FindParamIndex(string name)
        {
            if (paramNames == null) return -1;
            for (var i = 0; i < paramNames.Length; i++)
            {
                if (paramNames[i] == name) return i;
            }
            return -1;
        }

        private bool _GestureParamInAnimator()
        {
            if (animator == null || animator.parameters == null) return false;
            foreach (var p in animator.parameters)
            {
                if (p.name == GestureLeftName || p.name == GestureRightName) return true;
            }
            return false;
        }

        // A half-imported prop can leave these null; treat missing arrays as empty.
        private void _EnsureArrays()
        {
            if (paramNames == null) paramNames = new string[0];
            if (paramTypes == null) paramTypes = new int[0];
            if (paramDefaults == null) paramDefaults = new float[0];
            if (paramSynced == null) paramSynced = new bool[0];
        }

        // Splits params into synced (owner-written, replicated) and local slots, then caches
        // their Animator hashes so the per-frame write path is pure array reads.
        private void _InitParamSlots()
        {
            var count = paramNames.Length;
            var syncedCount = 0;
            for (var i = 0; i < count; i++)
            {
                if (i < paramSynced.Length && paramSynced[i]) syncedCount++;
            }

            _syncedValues = new float[syncedCount];
            _localValues = new float[count - syncedCount];
            _syncedSlot = new int[count];
            _localSlot = new int[count];
            _paramHashes = new int[count];

            var syncSlot = 0;
            var localSlot = 0;
            for (var i = 0; i < count; i++)
            {
                _paramHashes[i] = Animator.StringToHash(paramNames[i]);
                _syncedSlot[i] = -1;
                _localSlot[i] = -1;

                var def = i < paramDefaults.Length ? paramDefaults[i] : 0f;
                if (i < paramSynced.Length && paramSynced[i])
                {
                    _syncedSlot[i] = syncSlot;
                    _syncedValues[syncSlot] = def;
                    syncSlot++;
                }
                else
                {
                    _localSlot[i] = localSlot;
                    _localValues[localSlot] = def;
                    localSlot++;
                }
            }
        }

        public override void OnDeserialization()
        {
            _ApplyAllToAnimator();
        }

        /// <summary>
        /// True when the local player is the owner of this controller's object. The same
        /// check _SetParam uses for synced-param writes, so there is one consistent owner
        /// across menu access and parameter writes. Owner is derived from VRChat's native
        /// per-object ownership (no custom synced owner state), so late joiners and owner
        /// reassignment after a player leaves converge automatically.
        /// </summary>
        private bool _IsOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Ownership loss (drop, takeover, or the owner leaving) hides any open
            // menu/puppet/hand-gesture UI so a non-owner never sees or drives the prop.
            if (!_IsOwner()) _CloseAllMenus();
        }

        /// <summary>
        /// Sets a parameter by index using its float representation. Synced parameters
        /// are only written by the owner; non-owner writes are ignored.
        /// </summary>
        public void _SetParam(int index, float value)
        {
            if (paramNames == null || index < 0 || index >= paramNames.Length) return;

            if (index < paramSynced.Length && paramSynced[index])
            {
                if (!Networking.IsOwner(gameObject)) return;
                _syncedValues[_syncedSlot[index]] = value;
                RequestSerialization();
            }
            else
            {
                _localValues[_localSlot[index]] = value;
            }

            _ApplyToAnimator(index, value);
        }

        public void _SetFloatParam(int index, float value)
        {
            _SetParam(index, value);
        }

        public void _SetIntParam(int index, int value)
        {
            _SetParam(index, value);
        }

        public void _SetBoolParam(int index, bool value)
        {
            _SetParam(index, value ? 1f : 0f);
        }

        /// <summary>Returns the current value of a parameter as a float.</summary>
        public float _GetParam(int index)
        {
            if (paramNames == null || index < 0 || index >= paramNames.Length) return 0f;
            if (index < paramSynced.Length && paramSynced[index]) return _syncedValues[_syncedSlot[index]];
            return _localValues[_localSlot[index]];
        }

        public int _GetParamCount()
        {
            return paramNames == null ? 0 : paramNames.Length;
        }

        /// <summary>Resets all parameters to their defaults. Only the owner's writes are synced.</summary>
        public void _ResetParameters()
        {
            if (paramNames == null) return;

            var owner = Networking.IsOwner(gameObject);
            var changed = false;
            for (var i = 0; i < paramNames.Length; i++)
            {
                var def = i < paramDefaults.Length ? paramDefaults[i] : 0f;
                if (i < paramSynced.Length && paramSynced[i])
                {
                    if (!owner) continue;
                    _syncedValues[_syncedSlot[i]] = def;
                    changed = true;
                }
                else
                {
                    _localValues[_localSlot[i]] = def;
                }
            }

            if (owner && changed) RequestSerialization();
            _ApplyAllToAnimator();
        }

        public int _GetMenuCount()
        {
            if (menuControlStart == null) return 0;
            return menuControlStart.Length > 0 ? menuControlStart.Length - 1 : 0;
        }

        public int _GetCurrentMenu()
        {
            return _currentMenu;
        }

        public int _GetCurrentMenuControlCount()
        {
            var count = _TopLevelBaseControlCount();
            if (_currentMenu == 0 && _HandGesturesVisible()) count++;
            return count;
        }

        /// <summary>Base control count for the current level, reserving a slot for the Hand Gestures wedge at the top level.</summary>
        private int _TopLevelBaseControlCount()
        {
            var count = _NextControlStart() - _CurrentControlStart();
            if (count < 0) count = 0;
            if (_currentMenu == 0 && _HandGesturesVisible() && count > MaxMenuControls - 1) count = MaxMenuControls - 1;
            return count;
        }

        private bool _HandGesturesVisible()
        {
            return enableHandGestureEmulation && _animatorUsesHandGestures;
        }

        private bool _IsHandGesturesSlot(int controlIndex)
        {
            return _currentMenu == 0 && _HandGesturesVisible() && controlIndex == _TopLevelBaseControlCount();
        }

        public string _GetControlName(int controlIndex)
        {
            if (_IsHandGesturesSlot(controlIndex)) return HandGesturesControlName;

            var flat = _CurrentControlStart() + controlIndex;
            if (controlNames == null || flat < 0 || flat >= controlNames.Length) return "";
            return controlNames[flat];
        }

        public Texture2D _GetControlIcon(int controlIndex)
        {
            if (_IsHandGesturesSlot(controlIndex)) return null;

            var flat = _CurrentControlStart() + controlIndex;
            if (controlIcons == null || flat < 0 || flat >= controlIcons.Length) return null;
            return controlIcons[flat];
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
            _RefreshMenuView();
        }

        public void _Back()
        {
            if (_menuStackDepth <= 0)
            {
                _currentMenu = 0;
                _RefreshMenuView();
                return;
            }

            _menuStackDepth--;
            _currentMenu = _menuStack[_menuStackDepth];
            _RefreshMenuView();
        }

        /// <summary>Refreshes the menu view with the current menu level's controls.</summary>
        private void _RefreshMenuView()
        {
            if (menuView == null) return;

            var count = _GetCurrentMenuControlCount();
            if (count < 0) count = 0;
            if (count > MaxMenuControls) count = MaxMenuControls;

            var names = new string[count];
            var icons = new Texture2D[count];
            for (var i = 0; i < count; i++)
            {
                names[i] = _GetControlName(i);
                icons[i] = _GetControlIcon(i);
            }

            menuView.SetContent(names, icons);
        }

        public void _SetMenuVisible(bool visible)
        {
            if (menuView == null) return;
            if (visible) _PlaceMenuView();
            menuView._SetVisible(visible);
        }

        public void _ToggleMenu()
        {
            if (!_IsOwner()) return;

            if (_activePuppetFlat >= 0 || _activeHandGestures)
            {
                _OnPuppetClose();
                return;
            }

            if (menuView == null) return;
            if (!menuView.gameObject.activeSelf) _PlaceMenuView();
            menuView._ToggleVisible();
        }

        /// <summary>
        /// Moves the radial menu in front of the player's head (like the puppet controls) so it is
        /// immediately visible instead of sitting at the prop's origin. The menu reads from its -Z
        /// side, so that face is turned toward the player. Does nothing when there is no local
        /// player yet.
        /// </summary>
        private void _PlaceMenuView()
        {
            if (menuView == null) return;
            var player = Networking.LocalPlayer;
            if (player == null) return;

            var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            var pos = head.position + head.rotation * Vector3.forward * 1.0f;
            pos.y -= 0.5f;

            menuView.transform.position = pos;
            menuView.transform.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
        }

        public override void Interact()
        {
            if (!interactTogglesMenu) return;
            if (!_IsOwner()) return;
            _ToggleMenu();
        }

        /// <summary>Handles a press on a control within the current menu level.</summary>
        public void _OnControlPressed(int controlIndex)
        {
            if (!_IsOwner()) return;

            var count = _GetCurrentMenuControlCount();
            if (controlIndex < 0 || controlIndex >= count) return;

            if (_IsHandGesturesSlot(controlIndex))
            {
                _OpenHandGestureMenu();
                return;
            }

            var start = _CurrentControlStart();
            var flat = start + controlIndex;
            if (controlTypes == null || flat >= controlTypes.Length) return;

            var type = controlTypes[flat];
            if (type == ControlButton)
            {
                var param = _ControlParam(flat);
                if (param >= 0) _SetParam(param, _ControlValue(flat));
            }
            else if (type == ControlToggle)
            {
                var param = _ControlParam(flat);
                if (param < 0) return;

                var value = _ControlValue(flat);
                var current = _GetParam(param);
                if (Mathf.Abs(current - value) < ToggleEpsilon)
                    _SetParam(param, param < paramDefaults.Length ? paramDefaults[param] : 0f);
                else
                    _SetParam(param, value);
            }
            else if (type == ControlSubMenu)
            {
                var subMenu = _ControlSubmenu(flat);
                if (subMenu >= 0) _OpenMenu(subMenu);
            }
            else if (type == ControlBack)
            {
                _Back();
            }
            else if (type == ControlTwoAxis || type == ControlFourAxis || type == ControlRadialPuppet)
            {
                _OpenPuppet(flat);
            }
        }

        /// <summary>
        /// Opens the world-space puppet control for the given control and hides the menu.
        /// The puppet writes into this controller's params via the typed handler callbacks.
        /// </summary>
        private void _OpenPuppet(int flat)
        {
            if (!_IsOwner()) return;

            var type = controlTypes != null && flat < controlTypes.Length ? controlTypes[flat] : -1;
            UdonSharpBehaviour puppet = null;
            if (type == ControlRadialPuppet) puppet = radialPuppet;
            else if (type == ControlTwoAxis || type == ControlFourAxis) puppet = axisPuppet;
            if (puppet == null) return;

            _activePuppetFlat = flat;
            _SetMenuVisible(false);

            if (radialPuppet != null) radialPuppet.gameObject.SetActive(puppet == radialPuppet);
            if (axisPuppet != null) axisPuppet.gameObject.SetActive(puppet == axisPuppet);

            var name = controlNames != null && flat < controlNames.Length ? controlNames[flat] : "";

            if (type == ControlRadialPuppet)
            {
                var radial = (RadialPuppet)puppet;
                if (radial != null)
                {
                    radial.Label = name;
                    var p0 = _PuppetSubParam(flat, 0);
                    if (p0 >= 0) radial.Value = _GetParam(p0);
                }
            }
            else
            {
                var axis = (AxisPuppet)puppet;
                if (axis != null)
                {
                    axis.Label = name;
                    axis.AxisPuppetType = type == ControlTwoAxis ? AxisPuppetType.Two : AxisPuppetType.Four;

                    if (type == ControlTwoAxis)
                    {
                        var px = _PuppetSubParam(flat, 0);
                        var py = _PuppetSubParam(flat, 1);
                        if (px >= 0 && py >= 0)
                            axis.PuppetValue = new Vector2((_GetParam(px) + 1f) * 0.5f, (_GetParam(py) + 1f) * 0.5f);
                    }
                    else
                    {
                        axis.PuppetValue = new Vector2(0.5f, 0.5f);
                    }
                }
            }

            var player = Networking.LocalPlayer;
            if (player != null)
            {
                var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                var pos = head.position + head.rotation * Vector3.forward * 0.8f;
                pos.y -= 0.4f;
                puppet.transform.position = pos;
                // World-space UI reads from its -Z face, so point +Z away from the user's head.
                puppet.transform.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
            }
        }

        public override void _OnPuppetRadial(float value)
        {
            if (!_IsOwner()) return;
            _WritePuppetSubParam(0, value);
        }

        public override void _OnPuppetTwo(float x, float y)
        {
            if (!_IsOwner()) return;
            _WritePuppetSubParam(0, x);
            _WritePuppetSubParam(1, y);
        }

        public override void _OnPuppetFour(float negX, float posX, float negY, float posY)
        {
            if (!_IsOwner()) return;
            _WritePuppetSubParam(0, negX);
            _WritePuppetSubParam(1, posX);
            _WritePuppetSubParam(2, negY);
            _WritePuppetSubParam(3, posY);
        }

        public override void _OnPuppetClose()
        {
            if (!_IsOwner()) return;

            _activePuppetFlat = -1;
            _activeHandGestures = false;
            if (radialPuppet != null) radialPuppet.gameObject.SetActive(false);
            if (axisPuppet != null) axisPuppet.gameObject.SetActive(false);
            if (handGestures != null) handGestures.gameObject.SetActive(false);
            _SetMenuVisible(true);
        }

        /// <summary>
        /// Hides every piece of menu UI (radial menu, puppets, hand-gesture panel) and resets the
        /// active-control state. Called when the local player loses ownership so a non-owner never
        /// sees or drives the prop. Null-safe and idempotent.
        /// </summary>
        private void _CloseAllMenus()
        {
            _activePuppetFlat = -1;
            _activeHandGestures = false;
            _SetMenuVisible(false);
            if (radialPuppet != null) radialPuppet.gameObject.SetActive(false);
            if (axisPuppet != null) axisPuppet.gameObject.SetActive(false);
            if (handGestures != null) handGestures.gameObject.SetActive(false);
        }

        /// <summary>
        /// Opens the world-space hand gesture menu and hides the menu. Seeds the current left/right
        /// gesture from the synced GestureLeft/GestureRight params when present.
        /// </summary>
        private void _OpenHandGestureMenu()
        {
            if (!_IsOwner()) return;
            if (handGestures == null) return;

            _activeHandGestures = true;
            _SetMenuVisible(false);

            if (radialPuppet != null) radialPuppet.gameObject.SetActive(false);
            if (axisPuppet != null) axisPuppet.gameObject.SetActive(false);
            handGestures.gameObject.SetActive(true);

            if (_gestureLeftIndex >= 0) handGestures.LeftGesture = Mathf.RoundToInt(_GetParam(_gestureLeftIndex));
            if (_gestureRightIndex >= 0) handGestures.RightGesture = Mathf.RoundToInt(_GetParam(_gestureRightIndex));

            var player = Networking.LocalPlayer;
            if (player != null)
            {
                var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                var pos = head.position + head.rotation * Vector3.forward * 0.8f;
                pos.y -= 0.4f;
                handGestures.transform.position = pos;
                // World-space UI reads from its -Z face, so point +Z away from the user's head.
                handGestures.transform.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
            }
        }

        /// <summary>
        /// Writes the selected gestures into the GestureLeft/GestureRight params (synced, owner-gated).
        /// Each hand is only written if the prop's Animator uses that parameter.
        /// </summary>
        public override void _OnHandGesture(int left, int right)
        {
            if (_gestureLeftIndex >= 0) _SetIntParam(_gestureLeftIndex, left);
            if (_gestureRightIndex >= 0) _SetIntParam(_gestureRightIndex, right);
        }

        private void _WritePuppetSubParam(int index, float value)
        {
            var param = _PuppetSubParam(_activePuppetFlat, index);
            if (param >= 0) _SetParam(param, value);
        }

        private int _PuppetSubParam(int flat, int index)
        {
            if (flat < 0 || controlSubParamStart == null || controlSubParams == null) return -1;
            if (flat >= controlSubParamStart.Length) return -1;
            if (index >= _PuppetSubParamCount(flat)) return -1;

            var start = controlSubParamStart[flat];
            if (start < 0 || start + index >= controlSubParams.Length) return -1;
            return controlSubParams[start + index];
        }

        private int _PuppetSubParamCount(int flat)
        {
            if (controlTypes == null || flat < 0 || flat >= controlTypes.Length) return 0;
            var type = controlTypes[flat];
            if (type == ControlTwoAxis) return 2;
            if (type == ControlFourAxis) return 4;
            if (type == ControlRadialPuppet) return 1;
            return 0;
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

        private int _ControlParam(int flat)
        {
            return controlParamIndex != null && flat < controlParamIndex.Length ? controlParamIndex[flat] : -1;
        }

        private float _ControlValue(int flat)
        {
            return controlValues != null && flat < controlValues.Length ? controlValues[flat] : 0f;
        }

        private int _ControlSubmenu(int flat)
        {
            return controlSubmenuIndex != null && flat < controlSubmenuIndex.Length ? controlSubmenuIndex[flat] : -1;
        }

        // Pushes every param's current value into the Animator. Used on start and whenever a
        // sync arrives so remote clients mirror the owner's values.
        private void _ApplyAllToAnimator()
        {
            if (animator == null || paramNames == null) return;

            for (var i = 0; i < paramNames.Length; i++)
            {
                var synced = i < paramSynced.Length && paramSynced[i];
                var value = synced ? _syncedValues[_syncedSlot[i]] : _localValues[_localSlot[i]];
                _ApplyToAnimator(i, value);
            }
        }

        private void _ApplyToAnimator(int index, float value)
        {
            if (animator == null || index >= _paramHashes.Length) return;

            var type = index < paramTypes.Length ? paramTypes[index] : ParamTypeFloat;
            var hash = _paramHashes[index];
            if (type == ParamTypeInt) animator.SetInteger(hash, (int)value);
            else if (type == ParamTypeBool) animator.SetBool(hash, value > 0.5f);
            else animator.SetFloat(hash, value);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }
#endif
    }
}
