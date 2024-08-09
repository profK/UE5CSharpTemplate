﻿namespace UnrealSharpWeaver;

public enum PropertyType
{
    Unknown,

    Bool,

    Int8,
    Int16,
    Int,
    Int64,

    Byte,
    UInt16,
    UInt32,
    UInt64,

    Double,
    Float,

    Enum,

    Interface,
    Struct,
    Class,

    Object,
    ObjectPtr,
    DefaultComponent,
    LazyObject,
    WeakObject,

    SoftClass,
    SoftObject,

    Delegate,
    MulticastInlineDelegate,
    MulticastSparseDelegate,

    Array,
    Map,
    Set,
        
    String,
    Name,
    Text,

    InternalNativeFixedSizeArray,
    InternalManagedFixedSizeArray
}