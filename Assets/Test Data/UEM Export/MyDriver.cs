using UdonExpressionDriver.Runtime;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class MyDriver : UEDBehaviour
{
    private DataDictionary parametersDict;
    
    // Animator controller
    private Animator _animator;

    private void Start()
    {
        // Get animator component
        _animator = GetComponent<Animator>();

        // Reset animator values to default
        ResetParameters();
    }

    [NetworkCallable]
    public void ResetParameters()
    {
        TestInt = _testIntDefault;
        TestFloat = _testFloatDefault;
        TestBool = _testBoolDefault;
    }
#region Synced Variables
    private readonly int _testFloatHash = Animator.StringToHash("testFloat");
    private readonly float _testFloatDefault = 0.75f;

    [UdonSynced][FieldChangeCallback(nameof(TestFloat))]
    private float _testFloat;
    public float TestFloat
    {
        get => _testFloat;
        set
        {
            _testFloat = value;
            _animator.SetFloat(_testFloatHash, value);
        }
    }
#endregion

#region Local Variables
    private readonly int _testIntHash = Animator.StringToHash("testInt");
    private readonly int _testIntDefault = 3;

    [FieldChangeCallback(nameof(TestInt))]
    private int _testInt;
    public int TestInt
    {
        get => _testInt;
        set
        {
            _testInt = value;
            _animator.SetInteger(_testIntHash, value);
        }
    }
    private readonly int _testBoolHash = Animator.StringToHash("testBool");
    private readonly bool _testBoolDefault = true;

    [FieldChangeCallback(nameof(TestBool))]
    private bool _testBool;
    public bool TestBool
    {
        get => _testBool;
        set
        {
            _testBool = value;
            _animator.SetBool(_testBoolHash, value);
        }
    }
#endregion
}