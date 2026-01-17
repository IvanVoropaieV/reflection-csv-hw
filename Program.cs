using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

[JsonObject(MemberSerialization.Fields)]
class F
{
    int i1;
    int i2;
    int i3;
    int i4;
    int i5;
    public int[] mas;

    public F()
    {
        i1 = 1; i2 = 2; i3 = 3; i4 = 4; i5 = 5;
        mas = new int[] { 1, 2 };
    }

    public F Get() => new F();
}


static class CsvSimple
{
    public static string Serialize(object obj)
    {
        var t = obj.GetType();
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        string header = string.Join(",", fields.Select(f => f.Name));

        string values = string.Join(",", fields.Select(f =>
        {
            object? val = f.GetValue(obj);

            if (val == null) return "";

            if (val is int[] arr)
                return string.Join(";", arr.Select(x => x.ToString(CultureInfo.InvariantCulture)));

            return Convert.ToString(val, CultureInfo.InvariantCulture) ?? "";
        }));

        return header + Environment.NewLine + values;
    }

    public static T Deserialize<T>(string csv) where T : new()
    {
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new FormatException("CSV должен содержать 2 строки: header + values");

        var names = lines[0].Split(',');
        var vals = lines[1].Split(',');

        var obj = new T();
        var t = typeof(T);
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var map = fields.ToDictionary(f => f.Name, f => f);

        for (int i = 0; i < names.Length && i < vals.Length; i++)
        {
            var name = names[i];
            var raw = vals[i];

            if (!map.TryGetValue(name, out var field))
                continue;

            object? value = ParseValue(raw, field.FieldType);
            field.SetValue(obj, value);
        }

        return obj;
    }

    private static object? ParseValue(string raw, Type type)
    {
        if (string.IsNullOrEmpty(raw))
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            return null;
        }

        if (type == typeof(int))
            return int.Parse(raw, CultureInfo.InvariantCulture);

        if (type == typeof(int[]))
        {
            var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        }

        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }
}

sealed class PrivateFieldsContractResolver : DefaultContractResolver
{
    protected override List<MemberInfo> GetSerializableMembers(Type objectType)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        return objectType
            .GetMembers(flags)
            .Where(m => m.MemberType == MemberTypes.Field) // достаточно полей
            .ToList();
    }
}


class Program
{
    static void Main()
    {
        const int iterations = 100_000;

        var f = new F();

        // --- CSV ---
        string csv = "";
        var tCsvSer = Measure(() =>
        {
            for (int i = 0; i < iterations; i++)
                csv = CsvSimple.Serialize(f);
        });
        Console.WriteLine($"CSV serialize ({iterations} iters) = {tCsvSer} ms");

        var tConsole = Measure(() =>
        {
            Console.WriteLine("\n--- CSV OUTPUT ---");
            Console.WriteLine(csv);
            Console.WriteLine("--- END ---\n");
        });
        Console.WriteLine($"Console output time = {tConsole} ms");

        F objCsv = null!;
        var tCsvDes = Measure(() =>
        {
            for (int i = 0; i < iterations; i++)
                objCsv = CsvSimple.Deserialize<F>(csv);
        });
        Console.WriteLine($"CSV deserialize ({iterations} iters) = {tCsvDes} ms");

        // --- JSON (Newtonsoft) ---
        var jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new PrivateFieldsContractResolver(),
            Formatting = Formatting.None
        };

        string json = "";
        var tJsonSer = Measure(() =>
        {
            for (int i = 0; i < iterations; i++)
                json = JsonConvert.SerializeObject(f, jsonSettings);
        });
        Console.WriteLine($"JSON (Newtonsoft) serialize ({iterations} iters) = {tJsonSer} ms");

        F objJson = null!;
        var tJsonDes = Measure(() =>
        {
            for (int i = 0; i < iterations; i++)
                objJson = JsonConvert.DeserializeObject<F>(json, jsonSettings)!;
        });
        Console.WriteLine($"JSON (Newtonsoft) deserialize ({iterations} iters) = {tJsonDes} ms");

        Console.WriteLine("\nSanity:");
        Console.WriteLine("objCsv != null: " + (objCsv != null));
        Console.WriteLine("objJson != null: " + (objJson != null));
        Console.WriteLine("\nJSON example:");
        Console.WriteLine(json);
    }

    static long Measure(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }
}
