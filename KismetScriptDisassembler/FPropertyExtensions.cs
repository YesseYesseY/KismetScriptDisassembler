using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CUE4Parse.UE4.Objects.UObject;

namespace KismetScriptDisassembler
{
    public static class FPropertyExtensions
    {
        public static string GetCPPType(this FProperty prop)
        {
            switch (prop)
            {
                case FBoolProperty boolprop:
                    return "bool";
                case FDoubleProperty:
                    return "double";
                case FStructProperty structprop:
                    return $"struct {structprop.Struct.Name}";
                case FObjectProperty objectprop:
                    return $"class {objectprop.PropertyClass.Name}*";
                case FInterfaceProperty objectprop:
                    return $"class {objectprop.InterfaceClass.Name}*";
                case FIntProperty:
                    return "int32";
                case FFloatProperty:
                    return "float";
                case FNameProperty:
                    return "FName";
                case FTextProperty:
                    return "FText";
                case FStrProperty:
                    return "FString";
            }
            return prop.GetType().Name + "/*TODO*/";
        }

        public static bool IsOutParm(this FProperty prop)
        {
            return prop.PropertyFlags.HasFlag(EPropertyFlags.OutParm);
        }

        public static bool IsParm(this FProperty prop)
        {
            return prop.PropertyFlags.HasFlag(EPropertyFlags.Parm);
        }
    }
}
