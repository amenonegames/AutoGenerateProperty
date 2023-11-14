namespace GetterSetter
{
    public enum AXS
    {
        PublicGet = 1,
        PublicGetSet = 1 << 1,
        PublicGetPrivateSet = 1 << 2,
        PrivateGet = 1 << 3,
        PrivateGetSet = 1 << 4,
        ProtectedGet = 1 << 5,
        ProtectedGetSet = 1 << 6,
        ProtectedGetPrivateSet = 1 << 7,
        InternalGet = 1 << 8,
        InternalGetSet = 1 << 9,
        InternalGetPrivateSet = 1 << 10,
        ProtectedInternalGet = 1 << 11,
        ProtectedInternalGetSet = 1 << 12,
        ProtectedInternalGetPrivateSet = 1 << 13,
    }
}