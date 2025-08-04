using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using Org.BouncyCastle.Asn1.X509;

namespace KismetScriptDisassembler
{
    public class DefaultKismetScriptDisassembler
    {
        string Indents = "";
        StreamWriter OutFile;
        UClass Class;

        public DefaultKismetScriptDisassembler(string OutputDir, UClass Class)
        {
            Directory.CreateDirectory(OutputDir);
            OutFile = new StreamWriter(Path.Join(OutputDir, $"{Class.Name}.txt"), false);
            this.Class = Class;
        }

        void AddIndent()
        {
            Indents += "    ";
        }

        void DropIndent()
        {
            Indents = Indents.Remove(Indents.Length - 4, 4);
        }

        void WriteLine(string? value, bool ignoreIndent = false) => OutFile.WriteLine($"{(ignoreIndent ? "" : Indents)}{value}");
        void Write(string? value, bool ignoreIndent = true) => OutFile.Write($"{(ignoreIndent ? "" :Indents)}{value}");
        void WriteLine() => OutFile.WriteLine();
        void Write(bool ignoreIndent = false) => Write("", ignoreIndent);

        public void Disassemble()
        {
            WriteLine($"class {Class.Name} : public {Class.SuperStruct.Name}");
            WriteLine("{");
            AddIndent();
            foreach (var child in Class.ChildProperties)
            {
                var prop = (FProperty)child;
                WriteLine($"{prop.GetCPPType(),-32} {child.Name};");
            }
            WriteLine();

            
            Dictionary<string, List<uint>> labels = new();
            foreach (var child in Class.Children)
            {
                if (!child.TryLoad(out UFunction func))
                    continue;

                foreach (var expr in func.ScriptBytecode)
                {
                    switch (expr)
                    {
                        case EX_Jump jump:
                            labels.GetOrAdd(func.Name).Add(jump.CodeOffset);
                            break;
                        case EX_FinalFunction finalfunc:
                            if (finalfunc.StackNode.Name.StartsWith("ExecuteUbergraph"))
                            {
                                labels.GetOrAdd(finalfunc.StackNode.Name).Add((uint)((EX_IntConst)finalfunc.Parameters[0]).Value);
                            }
                            break;
                    }
                }

            }

            foreach (var child in Class.Children)
            {
                if (!child.TryLoad(out UFunction func))
                    continue;

                FProperty? retprop = (FProperty?)func.ChildProperties.FirstOrDefault(e => ((FProperty)e!).PropertyFlags.HasFlag(EPropertyFlags.ReturnParm), null);
                Write($"{(retprop is null ? "void" : retprop.GetCPPType())} {func.Name}(", false);
                List<FProperty> propstocareabout = new();
                for (int i = 0; i < func.ChildProperties.Length; i++)
                {
                    var prop = (FProperty)func.ChildProperties[i];

                    if (prop.PropertyFlags.HasFlag(EPropertyFlags.Parm))
                        propstocareabout.Add(prop);
                }
                for (int i = 0; i < propstocareabout.Count; i++)
                {
                    var prop = propstocareabout[i];
                    Write($"{prop.GetCPPType()}{(prop.IsOutParm() ? "*" : "")} {prop.Name}");
                    if (i != propstocareabout.Count - 1)
                        Write(", ");
                }
                WriteLine(")", true);
                WriteLine("{");
                AddIndent();

                foreach (var expr in func.ScriptBytecode)
                {
                    try
                    {
                        if (labels.TryGetValue(func.Name, out var labellist) && labellist.Contains((uint)expr.StatementIndex))
                            WriteLine($"\n{func.Name}_{expr.StatementIndex:X}:", true);

                        ParseExpr(expr, func);
                    }
                    catch(Exception e)
                    {
                        OutFile.Dispose();
                        Console.WriteLine("Crash :(");
                        return;
                    }
                }
                DropIndent();
                WriteLine("}");
                WriteLine();
            }

            DropIndent();
            WriteLine("}");

            OutFile.Dispose();
        }

        public void ParseExpr(KismetExpression expr, UFunction CurrentFunction, KismetExpression? outer = null)
        {
            switch (expr)
            {
                /*
                 TODO:
                  * EX_BindDelegate
                  * EX_AddMulticastDelegate
                  * EX_RemoveMulticastDelegate
                  * EX_SwitchValue
                  * EX_SetArray
                 */
                case EX_LetBase letobj:
                    {
                        //var letobj = (EX_LetBase)expr;
                        Write();
                        ParseExpr(letobj.Variable, CurrentFunction, expr);
                        Write(" = ");
                        ParseExpr(letobj.Assignment, CurrentFunction, expr);
                        WriteLine(";", true);
                    }
                    break;

                case EX_LocalOutVariable localoutvar:
                    {
                        localoutvar.Variable.New!.ResolvedOwner!.Load<UStruct>()!.GetProperty(localoutvar.Variable.New!.Path[0], out FField field);
                        var prop = (FProperty)field;
                        Write($"*{prop.Name}");
                    }
                    break;

                case EX_InstanceVariable localoutvar:
                    {
                        var reso = localoutvar.Variable.New!.ResolvedOwner;
                        Write($"{localoutvar.Variable.New!.Path[0]}");
                    }
                    break;

                case EX_Return retur:
                    {
                        if (retur.ReturnExpression.Token == EExprToken.EX_Nothing)
                        {
                            WriteLine("return;");
                        }
                        else
                        {
                            Write("return ", false);
                            ParseExpr(retur.ReturnExpression, CurrentFunction, expr);
                            WriteLine(";", true);
                        }
                    }
                    break;

                case EX_LocalFinalFunction localfinalfunc:
                    if (localfinalfunc.StackNode.Name.StartsWith("ExecuteUbergraph") && localfinalfunc.Parameters.Length > 0)
                    {
                        Write($"goto {localfinalfunc.StackNode.Name}_", false);
                        ParseExpr(localfinalfunc.Parameters[0], CurrentFunction, expr);
                        WriteLine(";", true);
                    }
                    else
                    {
                        Write($"{localfinalfunc.StackNode.Name}(", false);
                        for (int i = 0; i < localfinalfunc.Parameters.Length; i++)
                        {
                            ParseExpr(localfinalfunc.Parameters[i], CurrentFunction, expr);
                            if (i != localfinalfunc.Parameters.Length - 1)
                                Write(", ");
                        }
                        WriteLine(");", true);
                    }
                    break;

                case EX_FinalFunction finalfunc:
                    if (outer is null)
                        Write();
                    string prefix = $"{finalfunc.StackNode.ResolvedObject.Outer.Name}::";
                    if (outer is not null && outer is EX_Context)
                        prefix = "";
                    Write($"{prefix}{finalfunc.StackNode.Name}(", true);
                    for (int i = 0; i < finalfunc.Parameters.Length; i++)
                    {
                        ParseExpr(finalfunc.Parameters[i], CurrentFunction, expr);
                        if (i != finalfunc.Parameters.Length - 1)
                            Write(", ");
                    }
                    Write(")");
                    if (outer is null)
                        WriteLine(";", true);
                    break;

                case EX_EndOfScript:
                    break;

                case EX_Self:
                    Write("this");
                    break;

                case EX_False:
                    Write("false");
                    break;

                case EX_True:
                    Write("true");
                    break;

                case EX_IntConst intconst:
                    if (outer is EX_LocalFinalFunction outeraslocal and not null)
                    {
                        if (outeraslocal.StackNode.Name.StartsWith("ExecuteUbergraph"))
                        {
                            Write($"{intconst.Value:X}");
                            break;
                        }
                    }
                    Write($"(int32){intconst.Value}");
                    break;

                case EX_FloatConst floatconst:
                    Write($"(float){floatconst.Value}");
                    break;

                case EX_NameConst nameconst:
                    Write($"FName({nameconst.Value})");
                    break;

                case EX_TextConst textconst:
                    Write("FText(\"");
                    if (textconst.Value.SourceString is not null)
                        ParseExpr(textconst.Value.SourceString, CurrentFunction, expr);
                    Write("\")");
                    break;

                case EX_VectorConst vectorconst:
                    Write($"FVector({vectorconst.Value.X}, {vectorconst.Value.Y}, {vectorconst.Value.Z})");
                    break;

                case EX_RotationConst rotationconst:
                    Write($"FRotator({rotationconst.Value.Pitch}, {rotationconst.Value.Yaw}, {rotationconst.Value.Roll})");
                    break;

                case EX_ObjectConst objectconst:
                    Write($"{objectconst.Value.Name}");
                    break;

                case EX_StructConst structconst:
                    Write($"{structconst.Struct.Name}()");
                    // TODO: Properties
                    break;

                case EX_NoObject pushexec:
                    Write("nullptr");
                    break;

                case EX_PushExecutionFlow pushexec:
                    //WriteLine($"PushExecutionFlow({pushexec.PushingAddress});");
                    break;

                case EX_PopExecutionFlow:
                    //WriteLine($"PopExecutionFlow();");
                    WriteLine($"return;");
                    break;

                case EX_PopExecutionFlowIfNot popifnot:
                    Write("if (!", false);
                    ParseExpr(popifnot.BooleanExpression, CurrentFunction, expr);
                    WriteLine(")", true);
                    AddIndent();
                    //WriteLine($"PopExecutionFlow();");
                    WriteLine($"return;");
                    DropIndent();
                    break;

                case EX_ComputedJump computejump:
                    Write($"goto(", false);
                    ParseExpr(computejump.CodeOffsetExpression, CurrentFunction, expr);
                    WriteLine($");", true);
                    break;

                case EX_StructMemberContext structmemcontext:
                    ParseExpr(structmemcontext.StructExpression, CurrentFunction, expr);
                    Write($".{structmemcontext.Property.New!.Path[0]}");
                    break;

                case EX_InterfaceContext interfacecontext:
                    ParseExpr(interfacecontext.InterfaceValue, CurrentFunction, expr);
                    break;

                case EX_Cast cast:
                    Write($"Cast({cast.ConversionType.ToString()}, ");
                    ParseExpr(cast.Target, CurrentFunction, expr);
                    Write(")");
                    break;

                case EX_CastBase castbase:
                    Write($"({castbase.ClassPtr.Name}*)");
                    ParseExpr(castbase.Target, CurrentFunction, expr);
                    break;

                case EX_VirtualFunction virtualfunc:
                    if (outer is null)
                        Write();
                    Write($"{virtualfunc.VirtualFunctionName}(");
                    for (int i = 0; i < virtualfunc.Parameters.Length; i++)
                    {
                        ParseExpr(virtualfunc.Parameters[i], CurrentFunction, expr);
                        if (i != virtualfunc.Parameters.Length - 1)
                            Write(", ");
                    }
                    Write(")", true);
                    if (outer is null)
                        WriteLine(";", true);
                    break;

                case EX_JumpIfNot jumpifnot:
                    Write($"if (!", false);
                    ParseExpr(jumpifnot.BooleanExpression, CurrentFunction, expr);
                    WriteLine(")", true);
                    AddIndent();
                    WriteLine($"goto {CurrentFunction.Name}_{jumpifnot.CodeOffset:X};");
                    DropIndent();
                    break;

                case EX_Jump jump:
                    WriteLine($"goto {CurrentFunction.Name}_{jump.CodeOffset:X};");
                    break;

                case EX_ByteConst byteconst:
                    Write($"(uint8){byteconst.Value}");
                    break;

                case EX_StringConst stringconst:
                    if (outer is EX_TextConst and not null)
                        Write(stringconst.Value);
                    else
                        Write($"FString(L\"{stringconst.Value}\")");
                    break;

                case EX_Context context:
                    if (outer is null)
                        Write();
                    ParseExpr(context.ObjectExpression, CurrentFunction, expr);
                    Write("->");
                    ParseExpr(context.ContextExpression, CurrentFunction, expr);
                    if (outer is null)
                        WriteLine(";", true);
                    break;

                case EX_Let let:
                    {
                        Write();
                        ParseExpr(let.Variable, CurrentFunction, expr);
                        Write(" = ");
                        ParseExpr(let.Assignment, CurrentFunction, expr);
                        WriteLine(";", true);
                    }
                    break;

                case EX_LetValueOnPersistentFrame letvalonperfra:
                    {
                        Write();
                        Write($"{letvalonperfra.DestinationProperty.New!.Path[0]} = ");
                        ParseExpr(letvalonperfra.AssignmentExpression, CurrentFunction, expr);
                        WriteLine("; // LetValueOnPersistentFrame", true);
                    }
                    break;

                case EX_LocalVariable localvar:
                    {
                        localvar.Variable.New!.ResolvedOwner!.Load<UStruct>()!.GetProperty(localvar.Variable.New!.Path[0], out FField field);
                        var prop = (FProperty)field!;
                        bool skipwriteprefix = outer is not null && ((outer is EX_LetBase temp && temp.Assignment == expr) || outer is EX_Let temp2 && temp2.Assignment == expr);
                        if (!skipwriteprefix)
                            skipwriteprefix = outer is not null && (outer is EX_Context or EX_FinalFunction or EX_JumpIfNot or EX_Cast or EX_InterfaceContext
                                or EX_StructMemberContext or EX_CastBase);
                        if (!skipwriteprefix)
                            skipwriteprefix = prop.IsParm();

                        Write($"{(skipwriteprefix ? "" : $"{prop.GetCPPType()} ")}" +
                            $"{localvar.Variable.New!.Path[0]}");
                    }
                    break;

                default:
                    WriteLine($"// {expr.Token.ToString()}");
                    break;
            }
        }
    }
}
    