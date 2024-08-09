﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using UnrealSharpWeaver.MetaData;
using UnrealSharpWeaver.TypeProcessors;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace UnrealSharpWeaver.NativeTypes;

public abstract class NativeDataSimpleType(TypeReference typeRef, string marshallerName, int arrayDim, PropertyType propertyType) 
    : NativeDataType(typeRef, arrayDim, propertyType)
{
    protected TypeReference MarshallerClass;
    protected MethodReference ToNative;
    protected MethodReference FromNative;
    
    private bool IsReference;
    public override bool IsPlainOldData => true;

    protected virtual TypeReference[] GetTypeParams()
    {
        return [WeaverHelper.ImportType(CSharpType)];
    }

    public override void PrepareForRewrite(TypeDefinition typeDefinition, FunctionMetaData? functionMetadata, PropertyMetaData propertyMetadata)
    {
        base.PrepareForRewrite(typeDefinition, functionMetadata, propertyMetadata);
        IsReference = propertyMetadata.IsOutParameter;

        TypeReference[] typeParams = GetTypeParams();
        
        if (marshallerName.EndsWith("`1"))
        {
            MarshallerClass = WeaverHelper.FindGenericTypeInAssembly(WeaverHelper.BindingsAssembly, WeaverHelper.UnrealSharpNamespace, marshallerName, typeParams);
        }
        else
        {
            //TODO: Make this prettier! :(
            {
                // Try to find the marshaller in the bindings assembly
                MarshallerClass = WeaverHelper.FindTypeInAssembly(WeaverHelper.BindingsAssembly, marshallerName, WeaverHelper.UnrealSharpNamespace, false);

                if (MarshallerClass == null)
                {
                    TypeDefinition propertyTypeDefinition = CSharpType.Resolve();
                    
                    // Try to find the marshaller in the bindings again, but with the namespace of the property type.
                    MarshallerClass = WeaverHelper.FindTypeInAssembly(WeaverHelper.BindingsAssembly, marshallerName, propertyTypeDefinition.Namespace, false);

                    // Finally, try to find the marshaller in the user assembly.
                    if (MarshallerClass == null)
                    {
                        MarshallerClass = WeaverHelper.FindTypeInAssembly(WeaverHelper.UserAssembly, marshallerName, propertyTypeDefinition.Namespace);
                    }
                }
            }
        }

        TypeDefinition marshallerTypeDefinition = MarshallerClass.Resolve();
        ToNative = WeaverHelper.FindMethod(marshallerTypeDefinition, "ToNative")!;
        FromNative = WeaverHelper.FindMethod(marshallerTypeDefinition, "FromNative")!;
        
        if (marshallerName.EndsWith("`1"))
        {
            ToNative = FunctionProcessor.MakeMethodDeclaringTypeGeneric(ToNative, typeParams);
            FromNative = FunctionProcessor.MakeMethodDeclaringTypeGeneric(FromNative, typeParams);
        }
        
        ToNative = WeaverHelper.ImportMethod(ToNative);
        FromNative = WeaverHelper.ImportMethod(FromNative);
    }

    protected override void CreateGetter(TypeDefinition type, MethodDefinition getter, FieldDefinition offsetField, FieldDefinition nativePropertyField)
    {
        ILProcessor processor = BeginSimpleGetter(getter);
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, null, offsetField);
        WriteMarshalFromNative(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0));
        EndSimpleGetter(processor, getter);
    }

    protected override void CreateSetter(TypeDefinition type, MethodDefinition setter, FieldDefinition offsetField, FieldDefinition nativePropertyField)
    {
        ILProcessor processor = BeginSimpleSetter(setter);
        Instruction loadValue = processor.Create(IsReference ? OpCodes.Ldarga : OpCodes.Ldarg, 1);
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, null, offsetField);
        WriteMarshalToNative(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0), loadValue);
        EndSimpleSetter(processor, setter);
    }

    public override void WriteLoad(ILProcessor processor, TypeDefinition type, Instruction loadBuffer, FieldDefinition offsetField, VariableDefinition localVar)
    {
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, loadBuffer, offsetField);
        WriteMarshalFromNative(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0));
        processor.Emit(OpCodes.Stloc, localVar);
    }

    public override void WriteLoad(ILProcessor processor, TypeDefinition type, Instruction loadBuffer, FieldDefinition offsetField, FieldDefinition destField)
    {
        processor.Emit(OpCodes.Ldarg_0);
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, loadBuffer, offsetField);
        WriteMarshalFromNative(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0));
        processor.Emit(OpCodes.Stfld, destField);
    }

    public override IList<Instruction>? WriteStore(ILProcessor processor, TypeDefinition type, Instruction loadBuffer, FieldDefinition offsetField, int argIndex, ParameterDefinition paramDefinition)
    {
        // Load parameter index onto the stack. First argument is always 1, because 0 is the instance.
        List<Instruction> source = [processor.Create(OpCodes.Ldarg, argIndex)];
        
        if (IsReference)
        {
            Instruction loadInstructionOutParam = WeaverHelper.CreateLoadInstructionOutParam(paramDefinition, propertyType);
            source.Add(loadInstructionOutParam);
        }
        
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, loadBuffer, offsetField);
        
        return WriteMarshalToNativeWithCleanup(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0), source.ToArray());
    }

    public override IList<Instruction>? WriteStore(ILProcessor processor, TypeDefinition type, Instruction loadBuffer, FieldDefinition offsetField, FieldDefinition srcField)
    {
        Instruction[] loadField =
        [
            processor.Create(OpCodes.Ldarg_0),
            processor.Create(IsReference ? OpCodes.Ldflda : OpCodes.Ldfld, srcField)
        ];
        
        Instruction[] loadBufferInstructions = GetArgumentBufferInstructions(processor, loadBuffer, offsetField);
        return WriteMarshalToNativeWithCleanup(processor, type, loadBufferInstructions, processor.Create(OpCodes.Ldc_I4_0), loadField);
    }
    
    public override void WriteMarshalToNative(ILProcessor processor, TypeDefinition type, Instruction[] loadBufferPtr, Instruction loadArrayIndex, Instruction[] loadSource)
    {
        foreach (var i in loadBufferPtr)
        {
            processor.Append(i);
        }
        
        processor.Append(loadArrayIndex);
        
        foreach (Instruction i in loadSource)
        {
            processor.Append(i);
        }
        
        processor.Emit(OpCodes.Call, ToNative);
    }

    public override void WriteMarshalFromNative(ILProcessor processor, TypeDefinition type, Instruction[] loadBufferPtr,
        Instruction loadArrayIndex)
    {
        foreach (var i in loadBufferPtr)
        {
            processor.Append(i);
        }
        
        processor.Append(loadArrayIndex);
        processor.Emit(OpCodes.Call, FromNative);
    }

    public override void EmitFixedArrayMarshallerDelegates(ILProcessor processor, TypeDefinition type)
    {
        TypeReference[] typeParams = [];
        
        if (marshallerName.EndsWith("`1"))
        {
            if (!CSharpType.IsGenericInstance)
            {
                typeParams = [CSharpType];
            }
            else
            {
                GenericInstanceType generic = (GenericInstanceType)CSharpType;
                typeParams = [WeaverHelper.UserAssembly.MainModule.ImportReference(generic.GenericArguments[0].Resolve())];
            }
        }

        EmitSimpleMarshallerDelegates(processor, marshallerName, typeParams);
    }
}