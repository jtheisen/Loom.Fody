using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using System.Collections.Generic;

public class ModuleWeaver
{
    public Action<String> LogInfo { get; set; }

    public ModuleDefinition ModuleDefinition { get; set; }

    public ModuleWeaver()
    {
        LogInfo = m => { };
    }

    const MethodAttributes PublicImplementationAttributes
        = MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Public
        | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;

    TypeDefinition previousPropertyImplementationIf;
    TypeReference loomAttributeType;

    public void Execute()
    {
        var types = ModuleDefinition.GetTypes();

        previousPropertyImplementationIf = types.Single(n => n.Name == "IPreviousPropertyImplementation`2");

        loomAttributeType = types.Where(t => t.Name == "LoomAttribute").Single();

        foreach (var type in types)
            PotentiallyWeaveType(type);
        
        LogInfo("Why do I never see this?");
    }

    void PotentiallyWeaveType(TypeDefinition @class)
    {
        var loomAttributes = @class.CustomAttributes.Where(a => a.AttributeType == loomAttributeType).ToArray();

        foreach (var loomAttribute in loomAttributes)
            WeaveType(@class, loomAttribute);
    }

    GenericInstanceType mixInInstanceType;

    void WeaveType(TypeDefinition @class, CustomAttribute loomAttribute)
    {
        WeaveMixIn(@class, loomAttribute, out var mixInField);
        WeaveProperties(@class, mixInField,  loomAttribute);
    }

    void WeaveProperties(TypeDefinition @class, FieldDefinition mixInField, CustomAttribute loomAttribute)
    {
        var arguments = loomAttribute.ConstructorArguments.ToList();

        var propertyImplementationGenericType = arguments[1].Value as TypeDefinition;

        foreach (var property in @class.Properties)
        {
            WeaveProperty(@class, mixInField, propertyImplementationGenericType, property);
        }
    }

    void WeaveProperty(TypeDefinition @class, FieldDefinition mixInField, TypeDefinition propertyImplementationTemplate, PropertyDefinition property)
    {
        var accessorType = new TypeDefinition($"", $"{property.Name}Accessor", TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit);
        accessorType.BaseType = propertyImplementationTemplate.BaseType; // just because it's value type
        var previousPropertyImplementationConcreteIf = new InterfaceImplementation(previousPropertyImplementationIf.MakeGenericType(property.PropertyType, @class));
        accessorType.Interfaces.Add(previousPropertyImplementationConcreteIf);

        var getPropertyNameMethod = new MethodDefinition("GetPropertyName", PublicImplementationAttributes, ModuleDefinition.TypeSystem.String);
        getPropertyNameMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, property.Name));
        getPropertyNameMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(getPropertyNameMethod);

        var getMethod = new MethodDefinition($"Get", PublicImplementationAttributes, property.PropertyType);
        getMethod.Parameters.Add(new ParameterDefinition("container", ParameterAttributes.None, @class));
        getMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        getMethod.Body.Instructions.Add(
            Instruction.Create(property.GetMethod.IsVirtual
            ? OpCodes.Callvirt : OpCodes.Call, property.GetMethod));
        getMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(getMethod);

        var setMethod = new MethodDefinition($"Set", PublicImplementationAttributes, ModuleDefinition.TypeSystem.Void);
        setMethod.Parameters.Add(new ParameterDefinition("container", ParameterAttributes.None, @class));
        setMethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, property.PropertyType));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        setMethod.Body.Instructions.Add(
            Instruction.Create(property.SetMethod.IsVirtual
            ? OpCodes.Callvirt : OpCodes.Call, property.SetMethod));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(setMethod);

        var propertyImplementationType =
            propertyImplementationTemplate.MakeGenericType(
                property.PropertyType, @class, accessorType);
        var getImplementationTemplate = propertyImplementationTemplate.Methods.Single(m => m.Name == "Get");
        var setImplementationTemplate = propertyImplementationTemplate.Methods.Single(m => m.Name == "Set");
        var getImplementation = getImplementationTemplate.MakeGeneric(property.PropertyType, @class, accessorType);
        var setImplementation = setImplementationTemplate.MakeGeneric(property.PropertyType, @class, accessorType);

        var implementationField = new FieldDefinition($"_{property.Name}Implementation", FieldAttributes.Private, propertyImplementationType);
        @class.Fields.Add(implementationField);


        var newGetMethod = new MethodDefinition($"new{property.GetMethod.Name}", property.GetMethod.Attributes, property.GetMethod.ReturnType);
        var getMethodInstructions = newGetMethod.Body.Instructions;
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, implementationField));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, mixInField));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Call, getImplementation));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ret));
        @class.Methods.Add(newGetMethod);

        var newSetMethod = new MethodDefinition($"new{property.SetMethod.Name}", property.SetMethod.Attributes, ModuleDefinition.TypeSystem.Void);
        newSetMethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, property.PropertyType));
        var setMethodInstructions = newSetMethod.Body.Instructions;
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, implementationField));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, mixInField));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Call, setImplementation));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ret));
        @class.Methods.Add(newSetMethod);

        property.SetMethod = newSetMethod;
        property.GetMethod = newGetMethod;

        @class.NestedTypes.Add(accessorType);
    }

    void WeaveMixIn(TypeDefinition @class, CustomAttribute loomAttribute, out FieldDefinition mixInField)
    {
        var arguments = loomAttribute.ConstructorArguments.ToList();

        var mixInGenericType = arguments[0].Value as TypeDefinition;

        var containerParameter = mixInGenericType.GenericParameters[0];

        var mixInTypeInstance = mixInInstanceType = mixInGenericType.MakeGenericInstanceType(@class);

        var mixInType = mixInTypeInstance.Resolve();

        foreach (var i in mixInType.Interfaces)
            @class.Interfaces.Add(i);

        mixInField = new FieldDefinition($"mixInField", FieldAttributes.Public | FieldAttributes.SpecialName, mixInTypeInstance);

        @class.Fields.Add(mixInField);

        // Other methods may also be useful:
        //foreach (var method in mixInGenericType.Methods)
        //{
        //    WeaveDelegate(@class, containerParameter, mixInField, method);
        //}

        foreach (var @event in mixInType.Events)
        {
            WeaveEvent(@class, containerParameter, mixInField, @event);
        }
    }

    void WeaveEvent(TypeDefinition @class, GenericParameter containerParameter, FieldDefinition mixInField, EventDefinition @event)
    {
        var delegateEvent = new EventDefinition(@event.Name, EventAttributes.None, @event.EventType);
        delegateEvent.AddMethod = WeaveDelegate(@class, containerParameter, mixInField, @event.AddMethod);
        delegateEvent.RemoveMethod = WeaveDelegate(@class, containerParameter, mixInField, @event.RemoveMethod);
        @class.Events.Add(delegateEvent);
    }

    MethodDefinition WeaveDelegate(TypeDefinition @class, GenericParameter containerParameter, FieldDefinition mixInField, MethodDefinition method)
    {
        var delegateMethod = new MethodDefinition(method.Name, PublicImplementationAttributes, method.ReturnType);
        var parameters = new List<ParameterDefinition>();
        foreach (var parameter in method.Parameters)
        {
            if (parameter.ParameterType == containerParameter)
                parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, @class));
            else
                parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        }
        foreach (var parameter in parameters)
            delegateMethod.Parameters.Add(parameter);
        var processor = delegateMethod.Body.GetILProcessor();
        processor.Append(Instruction.Create(OpCodes.Nop));
        processor.Append(Instruction.Create(OpCodes.Ldarg_0));
        processor.Append(Instruction.Create(OpCodes.Ldflda, mixInField));
        for (int i = 1; i <= parameters.Count; ++i)
        {
            processor.Append(GetLda(i));
        }
        processor.Append(Instruction.Create(OpCodes.Call, method.MakeGeneric(@class)));
        processor.Append(Instruction.Create(OpCodes.Nop));
        processor.Append(Instruction.Create(OpCodes.Ret));
        @class.Methods.Add(delegateMethod);
        return delegateMethod;
    }

    Instruction GetLda(Int32 i)
    {
        switch (i)
        {
            case 0: return Instruction.Create(OpCodes.Ldarg_0);
            case 1: return Instruction.Create(OpCodes.Ldarg_1);
            case 2: return Instruction.Create(OpCodes.Ldarg_2);
            case 3: return Instruction.Create(OpCodes.Ldarg_3);
            default:
                return Instruction.Create(OpCodes.Ldarg_S, (Byte)i);
        }
    }
}

public static class Extensions
{
    public static TypeReference MakeGenericType(this TypeReference self, params TypeReference[] arguments)
    {
        if (self.GenericParameters.Count != arguments.Length)
            throw new ArgumentException();

        var instance = new GenericInstanceType(self);
        foreach (var argument in arguments)
            instance.GenericArguments.Add(argument);

        return instance;
    }

    public static MethodReference MakeGeneric(this MethodReference self, params TypeReference[] arguments)
    {
        var reference = new MethodReference(self.Name, self.ReturnType)
        {
            DeclaringType = self.DeclaringType.MakeGenericInstanceType(arguments),
            HasThis = self.HasThis,
            ExplicitThis = self.ExplicitThis,
            CallingConvention = self.CallingConvention,
        };

        foreach (var parameter in self.Parameters)
            reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

        foreach (var generic_parameter in self.GenericParameters)
            reference.GenericParameters.Add(new GenericParameter(generic_parameter.Name, reference));

        return reference;
    }

    public static void AddRange<T>(this Mono.Collections.Generic.Collection<T> self, IEnumerable<T> range)
    {
        foreach (var item in range)
            self.Add(item);
    }
}
