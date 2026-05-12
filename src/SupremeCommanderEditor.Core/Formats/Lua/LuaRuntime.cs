using MoonSharp.Interpreter;

namespace SupremeCommanderEditor.Core.Formats.Lua;

/// <summary>
/// Configures a MoonSharp Script with the pseudo-functions used by SupCom Lua files:
/// STRING(), FLOAT(), BOOLEAN(), VECTOR3(), GROUP(), RECTANGLE().
/// </summary>
public static class LuaRuntime
{
    public static Script CreateScript()
    {
        var script = new Script(CoreModules.TableIterators | CoreModules.Basic | CoreModules.String);

        // STRING('value') -> returns the string
        script.Globals["STRING"] = (Func<string, string>)(s => s);

        // FLOAT(value) -> returns the number
        script.Globals["FLOAT"] = (Func<double, double>)(f => f);

        // BOOLEAN(value) -> returns the boolean
        script.Globals["BOOLEAN"] = (Func<bool, bool>)(b => b);

        // VECTOR3(x, y, z) -> returns a table {x, y, z}
        script.Globals["VECTOR3"] = (Func<double, double, double, Table>)((x, y, z) =>
        {
            var t = new Table(script);
            t.Set(1, DynValue.NewNumber(x));
            t.Set(2, DynValue.NewNumber(y));
            t.Set(3, DynValue.NewNumber(z));
            return t;
        });

        // GROUP { ... } -> returns the table as-is
        script.Globals["GROUP"] = (Func<Table, Table>)(t => t);

        // RECTANGLE(x0, y0, x1, y1) -> returns a table
        script.Globals["RECTANGLE"] = (Func<double, double, double, double, Table>)((x0, y0, x1, y1) =>
        {
            var t = new Table(script);
            t.Set(1, DynValue.NewNumber(x0));
            t.Set(2, DynValue.NewNumber(y0));
            t.Set(3, DynValue.NewNumber(x1));
            t.Set(4, DynValue.NewNumber(y1));
            return t;
        });

        return script;
    }

    public static string GetString(Table table, string key, string defaultValue = "")
    {
        var val = table.Get(key);
        return val.Type == DataType.String ? val.String : defaultValue;
    }

    public static double GetNumber(Table table, string key, double defaultValue = 0)
    {
        var val = table.Get(key);
        return val.Type == DataType.Number ? val.Number : defaultValue;
    }

    public static bool GetBool(Table table, string key, bool defaultValue = false)
    {
        var val = table.Get(key);
        return val.Type == DataType.Boolean ? val.Boolean : defaultValue;
    }

    public static System.Numerics.Vector3 GetVector3(Table table, string key)
    {
        var val = table.Get(key);
        if (val.Type != DataType.Table) return System.Numerics.Vector3.Zero;
        var t = val.Table;
        return new System.Numerics.Vector3(
            (float)t.Get(1).Number,
            (float)t.Get(2).Number,
            (float)t.Get(3).Number);
    }

    public static Table? GetTable(Table table, string key)
    {
        var val = table.Get(key);
        return val.Type == DataType.Table ? val.Table : null;
    }
}
