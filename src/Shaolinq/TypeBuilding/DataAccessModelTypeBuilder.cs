﻿// Copyright (c) 2007-2016 Thong Nguyen (tumtumtum@gmail.com)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Platform;
using Platform.Reflection;

namespace Shaolinq.TypeBuilding
{
	public class DataAccessModelTypeBuilder
		: BaseTypeBuilder
	{
		private FieldBuilder dictionaryFieldBuilder;

		public DataAccessModelTypeBuilder(AssemblyBuildContext assemblyBuildContext, ModuleBuilder moduleBuilder)
			: base(assemblyBuildContext, moduleBuilder)
		{
		}

		public virtual void BuildTypePhase1(Type baseType)
		{
			this.AssemblyBuildContext.DataAccessModelTypeBuilder = this.ModuleBuilder.DefineType(baseType.FullName, TypeAttributes.Class | TypeAttributes.Public, baseType);
			this.AssemblyBuildContext.DataAccessModelPropertiesTypeBuilderField = this.AssemblyBuildContext.DataAccessModelTypeBuilder.DefineField("$$$dataAccessModelProperties", this.AssemblyBuildContext.DataAccessModelPropertiesTypeBuilder, FieldAttributes.Public);
		}

		public virtual Type BuildTypePhase2()
		{
			var typeBuilder = this.AssemblyBuildContext.DataAccessModelTypeBuilder;
			var baseType = this.AssemblyBuildContext.DataAccessModelTypeBuilder.BaseType;

			// Build constructor for DataAccessModel
			var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, null);
			var ctorGenerator = constructorBuilder.GetILGenerator();
			ctorGenerator.Emit(OpCodes.Ldarg_0);
			ctorGenerator.Emit(OpCodes.Call, baseType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null));
			ctorGenerator.Emit(OpCodes.Ldarg_0);
			ctorGenerator.Emit(OpCodes.Newobj, this.AssemblyBuildContext.DataAccessModelPropertiesTypeBuilderField.FieldType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new Type[0], null));
			ctorGenerator.Emit(OpCodes.Stfld, this.AssemblyBuildContext.DataAccessModelPropertiesTypeBuilderField);
			ctorGenerator.Emit(OpCodes.Ret);

			var methodInfo = typeBuilder.BaseType.GetMethod("Initialise", BindingFlags.Instance | BindingFlags.NonPublic);
			var methodAttributes = MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
			methodAttributes |= methodInfo.Attributes & (MethodAttributes.Public | MethodAttributes.Private | MethodAttributes.Assembly | MethodAttributes.Family);

			var initialiseMethodBuilder = typeBuilder.DefineMethod(methodInfo.Name, methodAttributes, methodInfo.CallingConvention, methodInfo.ReturnType, Type.EmptyTypes);
			var initialiseGenerator = initialiseMethodBuilder.GetILGenerator();

			this.dictionaryFieldBuilder = typeBuilder.DefineField("$$$dataAccessObjectsByType", typeof(Dictionary<Type,IQueryable>), FieldAttributes.Private);

			initialiseGenerator.Emit(OpCodes.Ldarg_0);
			initialiseGenerator.Emit(OpCodes.Ldc_I4, baseType.GetProperties().Count());
			initialiseGenerator.Emit(OpCodes.Newobj, this.dictionaryFieldBuilder.FieldType.GetConstructor(new Type[] { typeof(int) }));
			initialiseGenerator.Emit(OpCodes.Stfld, this.dictionaryFieldBuilder);

			foreach (var propertyInfo in baseType.GetProperties())
			{
				var queryableAttribute = propertyInfo.GetFirstCustomAttribute<DataAccessObjectsAttribute>(true);
				
				if (queryableAttribute == null)
				{
					continue;
				}

				if (typeof(RelatedDataAccessObjects<>).IsAssignableFromIgnoreGenericParameters(propertyInfo.PropertyType))
				{
					throw new InvalidOperationException("DataAccessModel objects should not defined properties of type RelatedDataAccessObject<>");
				}

				if (propertyInfo.GetGetMethod().IsAbstract || propertyInfo.GetSetMethod().IsAbstract)
				{
					// Generate the field for the queryable
					var fieldBuilder = typeBuilder.DefineField("$$" + propertyInfo.Name, propertyInfo.PropertyType, FieldAttributes.Private);

					initialiseGenerator.Emit(OpCodes.Ldarg_0);
					initialiseGenerator.Emit(OpCodes.Ldarg_0);
					initialiseGenerator.Emit(OpCodes.Ldnull);
					initialiseGenerator.Emit(OpCodes.Newobj, propertyInfo.PropertyType.GetConstructor(new [] { typeof(DataAccessModel), typeof(Expression)}));
					initialiseGenerator.Emit(OpCodes.Stfld, fieldBuilder);
					
					// Add to dictionary
					initialiseGenerator.Emit(OpCodes.Ldarg_0);
					initialiseGenerator.Emit(OpCodes.Ldfld, this.dictionaryFieldBuilder);
					initialiseGenerator.Emit(OpCodes.Ldtoken, propertyInfo.PropertyType.GetGenericArguments()[0]);
					initialiseGenerator.Emit(OpCodes.Call, MethodInfoFastRef.TypeGetTypeFromHandleMethod);
					initialiseGenerator.Emit(OpCodes.Ldarg_0);
					initialiseGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
					initialiseGenerator.Emit(OpCodes.Callvirt, this.dictionaryFieldBuilder.FieldType.GetMethod("set_Item"));
					
					var propertyBuilder = typeBuilder.DefineProperty(propertyInfo.Name, propertyInfo.Attributes, propertyInfo.PropertyType, Type.EmptyTypes);

					// Implement get method

					if (propertyInfo.GetGetMethod() != null && propertyInfo.GetGetMethod().IsAbstract)
					{
						propertyBuilder.SetGetMethod(this.BuildPropertyMethod("get", propertyInfo, fieldBuilder));
					}

					// Implement set method

					if (propertyInfo.GetSetMethod() != null && propertyInfo.GetSetMethod().IsAbstract)
					{
						throw new InvalidOperationException($"The property '{baseType.Name}.{propertyInfo.Name}' should not have a setter because it is a [DataAccessObjects] property");
					}
				}
			}

			initialiseGenerator.Emit(OpCodes.Ret);

			this.BuildGetDataAccessObjectsMethod();
			
			return typeBuilder.CreateType();
		}

		protected virtual void BuildGetDataAccessObjectsMethod()
		{
			var typeBuilder = this.AssemblyBuildContext.DataAccessModelTypeBuilder;
			var method = typeBuilder.BaseType.GetMethods().First(c => c.Name == "GetDataAccessObjects" && !c.IsGenericMethod && c.GetParameters().Length == 1);
			var methodAttributes = (method.Attributes & ~(MethodAttributes.Abstract | MethodAttributes.NewSlot)) | MethodAttributes.Virtual;
			var methodBuilder = typeBuilder.DefineMethod(method.Name, methodAttributes, method.CallingConvention, method.ReturnType, method.GetParameters().Select(c => c.ParameterType).ToArray());
			
			var generator = methodBuilder.GetILGenerator();

			var lockObj = generator.DeclareLocal(typeof(object));
			var acquiredLock = generator.DeclareLocal(typeof(bool));
			var label = generator.DefineLabel();
			var local = generator.DeclareLocal(typeof(IQueryable));

			var returnLabel = generator.DefineLabel();
			generator.Emit(OpCodes.Ldc_I4_0);
			generator.Emit(OpCodes.Stloc, acquiredLock);
			
			generator.BeginExceptionBlock();

			var b = false;

			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Ldfld, this.dictionaryFieldBuilder);
			generator.Emit(OpCodes.Dup);
			generator.Emit(OpCodes.Stloc, lockObj);
			generator.Emit(OpCodes.Ldloca, acquiredLock);
			generator.Emit(OpCodes.Call, TypeUtils.GetMethod(() => Monitor.Enter(default(object), ref b)));
			
			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Ldfld, this.dictionaryFieldBuilder);
			generator.Emit(OpCodes.Ldarg_1);
			generator.Emit(OpCodes.Ldloca, local);
			generator.Emit(OpCodes.Callvirt, typeof(Dictionary<Type, IQueryable>).GetMethod("TryGetValue", new Type[] { typeof(Type), typeof(IQueryable).MakeByRefType() }));
			generator.Emit(OpCodes.Brfalse, label);
			generator.Emit(OpCodes.Nop);
			generator.Emit(OpCodes.Leave, returnLabel);
			
			generator.MarkLabel(label);

			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Ldarg_1);
			generator.Emit(OpCodes.Callvirt, TypeUtils.GetMethod<DataAccessModel>(c => c.CreateDataAccessObjects(default(Type))));
			generator.Emit(OpCodes.Stloc, local);
			
			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Ldfld, this.dictionaryFieldBuilder);
			generator.Emit(OpCodes.Ldarg_1);
			generator.Emit(OpCodes.Ldloc, local);
			generator.Emit(OpCodes.Callvirt, typeof(Dictionary<Type, IQueryable>).GetMethod("set_Item"));
			generator.Emit(OpCodes.Nop);
			generator.Emit(OpCodes.Nop);

			generator.BeginFinallyBlock();
			var boolValue = generator.DeclareLocal(typeof(bool));
			generator.Emit(OpCodes.Ldloc, acquiredLock);
			generator.Emit(OpCodes.Ldc_I4_0);
			generator.Emit(OpCodes.Ceq);
			generator.Emit(OpCodes.Stloc, boolValue);
			generator.Emit(OpCodes.Ldloc, boolValue);
			var endOfFinally = generator.DefineLabel();
			generator.Emit(OpCodes.Brtrue, endOfFinally);
			generator.Emit(OpCodes.Ldloc, lockObj);
			generator.Emit(OpCodes.Call, TypeUtils.GetMethod(() => Monitor.Exit(default(object))));
			generator.Emit(OpCodes.Nop);
			generator.MarkLabel(endOfFinally);
			generator.EndExceptionBlock();
			
			generator.MarkLabel(returnLabel);
			generator.Emit(OpCodes.Ldloc, local);
			generator.Emit(OpCodes.Ret);
		}

		protected virtual MethodBuilder BuildPropertyMethod(string methodType, PropertyInfo propertyInfo, FieldBuilder backingField)
		{
			MethodInfo method;
			var typeBuilder = this.AssemblyBuildContext.DataAccessModelTypeBuilder;
			var methodAttributes = MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
			methodAttributes |= (propertyInfo.GetGetMethod().Attributes & (MethodAttributes.Public | MethodAttributes.Private | MethodAttributes.Assembly | MethodAttributes.Family));
		
			switch (methodType)
			{
				case "get":
					method = propertyInfo.GetGetMethod();
					break;
				case "set":
					method = propertyInfo.GetSetMethod();
					break;
				default:
					throw new NotSupportedException(methodType);
			}

			var parameters = method.GetParameters().Select(x => x.ParameterType).ToArray();
			var methodBuilder = typeBuilder.DefineMethod(method.Name, methodAttributes, method.CallingConvention, method.ReturnType, parameters);
			var generator = methodBuilder.GetILGenerator();

			switch (methodType)
			{
				case "get":
					generator.Emit(OpCodes.Ldarg_0);
					generator.Emit(OpCodes.Ldfld, backingField);
					generator.Emit(OpCodes.Ret);
					break;
				case "set":
					generator.Emit(OpCodes.Ldarg_0);
					generator.Emit(OpCodes.Ldarg_1);
					generator.Emit(OpCodes.Stfld, backingField);
					generator.Emit(OpCodes.Ret);
					break;
			}

			return methodBuilder;
		}
	}
}
