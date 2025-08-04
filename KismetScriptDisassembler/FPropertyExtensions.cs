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
                    return $"struct F{structprop.Struct.Name}";
                case FClassProperty classProperty:
                    return "class UClass*";
                case FObjectProperty objectprop:
                    char prefix = 'U';
                    
                    // EWWWW
                    if (GlobalProvider.Provider.MappingsForGame is not null)
                    {
                        var basething = objectprop.PropertyClass.Name;
                        if (!GlobalProvider.Provider.MappingsForGame.Types.ContainsKey(basething))
                        {
                            var klass = objectprop.PropertyClass.ResolvedObject;
                            while (klass is not null && klass.Super is not null)
                            {
                                basething = klass.Super.Name.Text;
                                klass = klass.Super;
                            }
                        }

                        var curtype = GlobalProvider.Provider.MappingsForGame.Types[basething];
                        
                        while (curtype is not null)
                        {
                            if (curtype.Name == "Actor")
                            {
                                prefix = 'A';
                                break;
                            }

                            if (GlobalProvider.Provider.MappingsForGame.Types.ContainsKey(curtype.SuperType ?? ""))
                                curtype = GlobalProvider.Provider.MappingsForGame.Types[curtype.SuperType ?? ""];
                            else
                                curtype = null;
                        }
                    }
                    
                    return $"class {prefix}{objectprop.PropertyClass.Name}*";
                case FInterfaceProperty objectprop:
                    return $"class I{objectprop.InterfaceClass.Name}*";
                case FEnumProperty enumprop:
                    return $"{enumprop.Enum.Name}";
                case FArrayProperty arrayprop:
                    return $"TArray<{(arrayprop.Inner is not null ? arrayprop.Inner.GetCPPType() : "arrayprop.Inner is null ;-;")}>";
                case FByteProperty byteprop:
                    if (byteprop.Enum.IsNull)
                        return "uint8";
                    else
                        return $"TEnumAsByte<{byteprop.Enum.Name}>";
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
