using System;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using XamlX.Transform;
using XamlX.TypeSystem;

namespace XamlX.Ast
{
    public class XamlNullExtensionNode : XamlAstNode, IXamlAstValueNode, IXamlAstEmitableNode
    {
        public XamlNullExtensionNode(IXamlLineInfo lineInfo) : base(lineInfo)
        {
            Type = new XamlAstClrTypeReference(lineInfo, XamlPseudoType.Null);
        }

        public IXamlAstTypeReference Type { get; }
        public XamlNodeEmitResult Emit(XamlEmitContext context, IXamlXCodeGen codeGen)
        {
            codeGen.Generator.Emit(OpCodes.Ldnull);
            return XamlNodeEmitResult.Type(XamlPseudoType.Null);
        }
    }

    public class XamlTypeExtensionNode : XamlAstNode, IXamlAstValueNode, IXamlAstEmitableNode
    {
        private readonly IXamlType _systemType;

        public XamlTypeExtensionNode(IXamlLineInfo lineInfo, IXamlAstTypeReference value,
            IXamlType systemType) : base(lineInfo)
        {
            _systemType = systemType;
            Type = new XamlAstClrTypeReference(this, systemType);
            Value = value;
        }

        public IXamlAstTypeReference Type { get; }
        public IXamlAstTypeReference Value { get; set; }

        public override void VisitChildren(XamlXAstVisitorDelegate visitor)
        {
            Value = Value.Visit(visitor) as IXamlAstTypeReference;
        }

        public XamlNodeEmitResult Emit(XamlEmitContext context, IXamlXCodeGen codeGen)
        {
            var type = Value.GetClrType();
            var method = _systemType.Methods.FirstOrDefault(m =>
                m.Name == "GetTypeFromHandle" && m.Parameters.Count == 1 &&
                m.Parameters[0].Name == "RuntimeTypeHandle");

            if (method == null)
                throw new XamlTypeSystemException(
                    $"Unable to find GetTypeFromHandle(RuntimeTypeHandle) on {_systemType.GetFqn()}");
            codeGen.Generator
                .Emit(OpCodes.Ldtoken, type)
                .Emit(OpCodes.Call, method);
            return XamlNodeEmitResult.Type(_systemType);
        }
    }

    public class XamlStaticExtensionNode : XamlAstNode, IXamlAstValueNode, IXamlAstEmitableNode
    {
        public XamlStaticExtensionNode(XamlXAstNewInstanceNode lineInfo, IXamlAstTypeReference targetType, string member) : base(lineInfo)
        {
            TargetType = targetType;
            Member = member;
        }

        public string Member { get; set; }
        public IXamlAstTypeReference TargetType { get; set; }

        public override void VisitChildren(XamlXAstVisitorDelegate visitor)
        {
            TargetType = (IXamlAstTypeReference) TargetType.Visit(visitor);
        }

        IXamlMember ResolveMember(IXamlType type)
        {
            return type.Fields.FirstOrDefault(f => f.IsPublic && f.IsStatic && f.Name == Member) ??
                   (IXamlMember) type.Properties.FirstOrDefault(p =>
                       p.Name == Member && p.Getter != null && p.Getter.IsPublic && p.Getter.IsStatic);
        }
        
        public XamlNodeEmitResult Emit(XamlEmitContext context, IXamlXCodeGen codeGen)
        {
            var type = TargetType.GetClrType();
            var member = ResolveMember(type);
            if (member is IXamlProperty prop)
            {
                codeGen.Generator.Emit(OpCodes.Call, prop.Getter);
                return XamlNodeEmitResult.Type(prop.Getter.ReturnType);
            }
            else if (member is IXamlField field)
            {
                if (field.IsLiteral)
                {
                    var ftype = field.FieldType.IsEnum ? field.FieldType.GetEnumUnderlyingType() : field.FieldType;
                    
                    if (ftype.Name == "UInt64" || ftype.Name == "Int64")
                        codeGen.Generator.Emit(OpCodes.Ldc_I8,
                            TypeSystemHelpers.ConvertLiteralToLong(field.GetLiteralValue()));
                    else if (ftype.Name == "Double")
                        codeGen.Generator.Emit(OpCodes.Ldc_R8, (double) field.GetLiteralValue());
                    else if (ftype.Name == "Single")
                        codeGen.Generator.Emit(OpCodes.Ldc_R4, (float) field.GetLiteralValue());
                    else if (ftype.Name == "String")
                        codeGen.Generator.Emit(OpCodes.Ldstr, (string) field.GetLiteralValue());
                    else
                        codeGen.Generator.Emit(OpCodes.Ldc_I4,
                            TypeSystemHelpers.ConvertLiteralToInt(field.GetLiteralValue()));
                }
                else
                    codeGen.Generator.Emit(OpCodes.Ldsfld, field);
                return XamlNodeEmitResult.Type(field.FieldType);
            }
            else
                throw new XamlLoadException(
                    $"Unable to resolve {Member} as static field, property, constant or enum value", this);
        }

        IXamlType ResolveReturnType()
        {
            if (!(TargetType is XamlAstClrTypeReference type))
                return XamlPseudoType.Unknown;
            var member = ResolveMember(type.Type);
            if (member is IXamlField field)
                return field.FieldType;
            if (member is IXamlProperty prop && prop.Getter != null)
                return prop.Getter.ReturnType;
            return XamlPseudoType.Unknown;
        }
        
        public IXamlAstTypeReference Type => new XamlAstClrTypeReference(this, ResolveReturnType());
    }
}