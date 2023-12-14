﻿using System;
namespace AutoProperty
{
    /// <summary>
    /// This class is generated by AutoPropertyGenerator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field,
        Inherited = false, AllowMultiple = false)]
    sealed class AutoPropAttribute : Attribute
    {
    
        public Type Type { get; set; }
        public AXS AXSType { get; set; }
        public bool GenerateInterface { get; set; }
        
        public AutoPropAttribute(bool generateInterface = false)
        {
            GenerateInterface = generateInterface;
        }
        
        // デフォルトアクセスレベルを変える場合は、ここを変更する
        public AutoPropAttribute(AXS access = AXS.PublicGet,bool generateInterface = false)
        {
            AXSType = access;
            GenerateInterface = generateInterface;
        }

        // デフォルトアクセスレベルを変える場合は、ここを変更する
        public AutoPropAttribute(Type type, AXS access = AXS.PublicGet,bool generateInterface = false)
        {
            Type = type;
            AXSType = access;
        }
        
        public AutoPropAttribute(Type type , bool generateInterface = false)
        {
            Type = type;
            GenerateInterface = generateInterface;
        }
        

    }

    [Flags]
    internal enum AXS
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