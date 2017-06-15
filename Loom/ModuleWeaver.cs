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

    public class PropertyInformation
    {
        public PropertyDefinition Property { get; set; }
        public FieldDefinition Field { get; set; }
        public TypeDefinition AccessorType { get; set; }
    }

    const MethodAttributes PublicImplementationAttributes
        = MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Public
        | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;

    TypeDefinition previousPropertyImplementationIf;
    TypeReference loomAttributeType;

    public void Execute()
    {
        var types = ModuleDefinition.GetTypes();

        previousPropertyImplementationIf = types
            .Where(n => n.Name == "IPreviousPropertyImplementation`2")
            .Single("Can't find type IPreviousPropertyImplementation`2");

        loomAttributeType = types
            .Where(t => t.Name == "LoomAttribute")
            .Single("Can't find the LoomAttribute");

        foreach (var type in types)
            PotentiallyWeaveType(type);
    }

    void PotentiallyWeaveType(TypeDefinition @class)
    {
        var loomAttributes = @class.CustomAttributes.Where(a => a.AttributeType == loomAttributeType).ToArray();

        foreach (var loomAttribute in loomAttributes)
            WeaveType(@class, loomAttribute);
    }

    void WeaveType(TypeDefinition @class, CustomAttribute loomAttribute)
    {
        var propertyInformation = new PropertyInformation[@class.Properties.Count];

        var arguments = loomAttribute.ConstructorArguments.ToList();

        var propertyImplementationGenericType = arguments[1].Value as TypeDefinition;

        // Methods and events are taken from the MixIn's type directly rather than, as
        // would be more appropriate, the interfaces it implements. That's because that way
        // we don't need to resolve the interface's type.

        WeaveMixInWithEventDelegations(@class, loomAttribute, out var mixInType, out var mixInField);
        WeavePropertyDelegations(@class, mixInField, propertyImplementationGenericType, propertyInformation);
        WeaveMethodDelegations(@class, mixInType, mixInField, propertyImplementationGenericType, propertyInformation, loomAttribute);
    }

    void WeavePropertyDelegations(
        TypeDefinition @class, FieldDefinition mixInField,
        TypeDefinition propertyImplementationGenericType, PropertyInformation[] propertyInformation)
    {
        for (int i = 0; i < @class.Properties.Count; ++i)
        {
            propertyInformation[i]
                = WeaveProperty(@class, mixInField, propertyImplementationGenericType, i, @class.Properties[i]);
        }
    }

    PropertyInformation WeaveProperty(TypeDefinition @class, FieldDefinition mixInField, TypeDefinition propertyImplementationTemplate, Int32 index, PropertyDefinition property)
    {
        var oldGetMethod = new MethodDefinition($"old{property.GetMethod.Name}", property.GetMethod.Attributes, property.GetMethod.ReturnType);
        oldGetMethod.Body = property.GetMethod.Body;
        @class.Methods.Add(oldGetMethod);

        var oldSetMethod = new MethodDefinition($"old{property.SetMethod.Name}", property.SetMethod.Attributes, property.SetMethod.ReturnType);
        oldSetMethod.Body = property.SetMethod.Body;
        oldSetMethod.Parameters.AddRange(property.SetMethod.Parameters);
        @class.Methods.Add(oldSetMethod);

        var accessorType = new TypeDefinition($"", $"{property.Name}Accessor", TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit);
        accessorType.BaseType = propertyImplementationTemplate.BaseType; // just because it's a value type
        var previousPropertyImplementationConcreteIf = new InterfaceImplementation(previousPropertyImplementationIf.MakeGenericType(property.PropertyType, @class));
        accessorType.Interfaces.Add(previousPropertyImplementationConcreteIf);

        var getPropertyNameMethod = new MethodDefinition("GetPropertyName", PublicImplementationAttributes, ModuleDefinition.TypeSystem.String);
        getPropertyNameMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, property.Name));
        getPropertyNameMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(getPropertyNameMethod);

        var getIndexMethod = new MethodDefinition("GetIndex", PublicImplementationAttributes, ModuleDefinition.TypeSystem.Int32);
        getIndexMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, index));
        getIndexMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(getIndexMethod);

        var getMethod = new MethodDefinition($"Get", PublicImplementationAttributes, property.PropertyType);
        getMethod.Parameters.Add(new ParameterDefinition("container", ParameterAttributes.None, @class));
        getMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        getMethod.Body.Instructions.Add(
            Instruction.Create(property.GetMethod.IsVirtual
            ? OpCodes.Callvirt : OpCodes.Call, oldGetMethod));
        getMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(getMethod);

        var setMethod = new MethodDefinition($"Set", PublicImplementationAttributes, ModuleDefinition.TypeSystem.Void);
        setMethod.Parameters.Add(new ParameterDefinition("container", ParameterAttributes.None, @class));
        setMethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, property.PropertyType));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        setMethod.Body.Instructions.Add(
            Instruction.Create(property.SetMethod.IsVirtual
            ? OpCodes.Callvirt : OpCodes.Call, oldSetMethod));
        setMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        accessorType.Methods.Add(setMethod);

        var propertyImplementationType =
            propertyImplementationTemplate.MakeGenericType(
                property.PropertyType, @class, accessorType);
        var getImplementationTemplate = propertyImplementationTemplate.Methods.Single(m => m.Name == "Get");
        var setImplementationTemplate = propertyImplementationTemplate.Methods.Single(m => m.Name == "Set");
        // The methods don't appear to be generic themselves, but they're defined on the property implementation
        // generic type, which has three generic parameters.
        var getImplementation = getImplementationTemplate.MakeGeneric(property.PropertyType, @class, accessorType);
        var setImplementation = setImplementationTemplate.MakeGeneric(property.PropertyType, @class, accessorType);

        var implementationField = new FieldDefinition($"_{property.Name}Implementation", FieldAttributes.Private, propertyImplementationType);
        @class.Fields.Add(implementationField);

        property.GetMethod.Body = new MethodBody(property.GetMethod);
        var getMethodInstructions = property.GetMethod.Body.Instructions;
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, implementationField));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, mixInField));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Call, getImplementation));
        getMethodInstructions.Add(Instruction.Create(OpCodes.Ret));

        property.SetMethod.Body = new MethodBody(property.SetMethod);
        var setMethodInstructions = property.SetMethod.Body.Instructions;
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, implementationField));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldflda, mixInField));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Call, setImplementation));
        setMethodInstructions.Add(Instruction.Create(OpCodes.Ret));

        @class.NestedTypes.Add(accessorType);

        return new PropertyInformation
        {
            Property = property,
            Field = implementationField,
            AccessorType = accessorType
        };
    }

    void WeaveMixInWithEventDelegations(TypeDefinition @class, CustomAttribute loomAttribute,
        out TypeDefinition mixInType, out FieldDefinition mixInField)
    {
        var arguments = loomAttribute.ConstructorArguments.ToList();

        var mixInGenericType = arguments[0].Value as TypeDefinition;

        var containerParameter = mixInGenericType.GenericParameters[0];

        var mixInTypeInstance = mixInGenericType.MakeGenericInstanceType(@class);

        mixInType = mixInTypeInstance.Resolve();

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
        // method is on the MixIn type, which is a generic type with one type parameter (the container)
        processor.Append(Instruction.Create(OpCodes.Call, method.MakeGeneric(@class)));
        processor.Append(Instruction.Create(OpCodes.Nop));
        processor.Append(Instruction.Create(OpCodes.Ret));
        @class.Methods.Add(delegateMethod);
        return delegateMethod;
    }

    void WeaveMethodDelegations(
        TypeDefinition @class, TypeDefinition mixInType, FieldDefinition mixInField,
        TypeDefinition propertyImplementationTemplate, PropertyInformation[] propertyInformation,
        CustomAttribute loomAttribute)
    {
        foreach (var method in mixInType.Methods)
        {
            WeaveDelegateToProperties
                (@class, mixInField, propertyImplementationTemplate, propertyInformation, method);
        }
    }

    void WeaveDelegateToProperties(
        TypeDefinition @class,
        FieldDefinition mixInField,
        TypeDefinition propertyImplementationTemplate,
        PropertyInformation[] propertyInformation,
        MethodDefinition method)
    {
        var targetMethodOnProperties
            = propertyImplementationTemplate.Methods.FirstOrDefault(m => m.Name == method.Name);

        if (targetMethodOnProperties == null) return;

        if (method.ReturnType != targetMethodOnProperties.ReturnType)
            throw new Exception($"Property delegation method {method.Name} has different return types between the mixin and the property implementation.");

        if (method.Parameters.Count != targetMethodOnProperties.Parameters.Count)
            throw new Exception($"Property delegation method {method.Name} is expected to have the same number of parameters on the mixin than on the property implementation.");

        if (method.Parameters.Count == 0)
            throw new Exception($"Property delegation method {method.Name} lacks the index parameter on the mixin.");

        if (method.Parameters[0].ParameterType != ModuleDefinition.TypeSystem.Int32)
            throw new Exception($"Property delegation method {method.Name} is expected to have Int32 as its first parameter on the mixin.");

        for (int i = 1; i < method.Parameters.Count; ++i)
        {
            if (method.Parameters[i].ParameterType != targetMethodOnProperties.Parameters[i].ParameterType)
                throw new Exception($"Property delegation method {method.Name}'s parameter #{i} is different between the mixin and the property implementation.");
        }

        var newMethod = new MethodDefinition(method.Name, method.Attributes, method.ReturnType);
        var parameters = new List<ParameterDefinition>();
        for (int i = 0; i < method.Parameters.Count; ++i)
        {
            var p = method.Parameters[i];
            newMethod.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        }
        @class.Methods.Add(newMethod);
        var processor = newMethod.Body.GetILProcessor();

        processor.Emit(OpCodes.Ldarg_1);
        var switchInstruction = processor.AppendAndReturn(Instruction.Create(OpCodes.Switch, new Instruction[0]));

        processor.Emit(OpCodes.Ldarg_0);
        processor.Emit(OpCodes.Ldflda, mixInField);
        for (int ai = 1; ai <= method.Parameters.Count; ++ai)
            processor.Append(GetLda(ai));
        processor.Emit(OpCodes.Call, method.MakeGeneric(@class));
        processor.Emit(OpCodes.Ret);

        var callsites = new List<Instruction>();

        for (int pic = 0; pic < propertyInformation.Length; ++pic)
        {
            var info = propertyInformation[pic];

            var head = processor.AppendAndReturn(Instruction.Create(OpCodes.Ldarg_0));
            processor.Append(Instruction.Create(OpCodes.Ldflda, info.Field));
            processor.Append(Instruction.Create(OpCodes.Ldarg_0));
            for (int ai = 2; ai <= method.Parameters.Count; ++ai)
                processor.Append(GetLda(ai));
            // The methods don't appear to be generic themselves, but they're defined on the property implementation
            // generic type, which has three generic parameters.
            var concreteTargetMethodOnProperties
                = targetMethodOnProperties.MakeGeneric(info.Property.PropertyType, @class, info.AccessorType);
            processor.Append(Instruction.Create(OpCodes.Call, concreteTargetMethodOnProperties));
            processor.Append(Instruction.Create(OpCodes.Ret));

            callsites.Add(head);
        }

        switchInstruction.Operand = callsites.ToArray();
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

    public static T Single<T>(this IEnumerable<T> list, String msg)
    {
        try
        {
            return list.Single();
        }
        catch (Exception)
        {
            throw new WeavingException(msg);
        }
    }

    public static Instruction AppendAndReturn(this ILProcessor processor, Instruction instruction)
    {
        processor.Append(instruction);
        return instruction;
    }
}

public class WeavingException : Exception
{
    public WeavingException(string message) : base(message)
    {

    }
}
