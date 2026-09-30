using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

/// <summary>
/// ArgvBuilder 的单测。这是全项目最容易出错的组件，规范里的每条展开规则都要有对应用例。
/// </summary>
public class ArgvBuilderTests
{
    private static ManifestAction Action(params ManifestField[] fields) => new()
    {
        Id = "test",
        Title = "测试动作",
        Command = "cmd",
        Fields = [.. fields],
    };

    private static Dictionary<string, object?> Values(params (string Id, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.Value, StringComparer.Ordinal);

    // ---------------------------------------------------------------- 基本顺序

    [Fact]
    public void 命令之后紧接_commandArgs_然后是字段_最后是_fixedArgs()
    {
        var action = new ManifestAction
        {
            Id = "t",
            Title = "t",
            Command = "x",
            CommandArgs = ["--always"],
            FixedArgs = ["--hidden"],
            Fields =
            [
                new ManifestField { Id = "archive", Label = "包", Type = "file", Style = FieldStyle.Positional },
            ],
        };

        var argv = ArgvBuilder.Build(action, Values(("archive", "a.zip")));

        Assert.Equal(["x", "--always", "a.zip", "--hidden"], argv);
    }

    [Fact]
    public void 字段按声明顺序展开_不按值的传入顺序()
    {
        var action = Action(
            new ManifestField { Id = "first", Label = "一", Type = "text", Style = FieldStyle.Positional },
            new ManifestField { Id = "second", Label = "二", Type = "text", Style = FieldStyle.Positional });

        var argv = ArgvBuilder.Build(action, Values(("second", "B"), ("first", "A")));

        Assert.Equal(["cmd", "A", "B"], argv);
    }

    // ---------------------------------------------------------------- 空值规则

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空值不产生任何_token(object? empty)
    {
        var action = Action(
            new ManifestField { Id = "a", Label = "a", Type = "text", Style = FieldStyle.Positional },
            new ManifestField { Id = "b", Label = "b", Type = "text", Style = FieldStyle.Attached, Prefix = "-o" });

        var argv = ArgvBuilder.Build(action, Values(("a", empty), ("b", empty)));

        Assert.Equal(["cmd"], argv);
    }

    [Fact]
    public void 空集合不产生任何_token()
    {
        var action = Action(
            new ManifestField { Id = "x", Label = "x", Type = "multiselect", Style = FieldStyle.Repeated, Prefix = "-x!" });

        var argv = ArgvBuilder.Build(action, Values(("x", new List<string>())));

        Assert.Equal(["cmd"], argv);
    }

    [Fact]
    public void 值未提供时不产生_token()
    {
        var action = Action(
            new ManifestField { Id = "a", Label = "a", Type = "text", Style = FieldStyle.Positional });

        var argv = ArgvBuilder.Build(action, Values());

        Assert.Equal(["cmd"], argv);
    }

    // ---------------------------------------------------------------- 各风格

    [Fact]
    public void flag_为真时输出前缀_为假时什么都不输出()
    {
        var action = Action(
            new ManifestField { Id = "y", Label = "y", Type = "bool", Style = FieldStyle.Flag, Prefix = "-y" });

        Assert.Equal(["cmd", "-y"], ArgvBuilder.Build(action, Values(("y", true))));
        Assert.Equal(["cmd"], ArgvBuilder.Build(action, Values(("y", false))));
    }

    [Fact]
    public void flag_能接受从_YAML_读来的字符串_true()
    {
        var action = Action(
            new ManifestField { Id = "y", Label = "y", Type = "bool", Style = FieldStyle.Flag, Prefix = "-y" });

        Assert.Equal(["cmd", "-y"], ArgvBuilder.Build(action, Values(("y", "true"))));
    }

    [Fact]
    public void attached_把前缀和值贴成一个_token()
    {
        var action = Action(
            new ManifestField { Id = "o", Label = "o", Type = "directory", Style = FieldStyle.Attached, Prefix = "-o" });

        var argv = ArgvBuilder.Build(action, Values(("o", @"D:\out")));

        Assert.Equal(["cmd", @"-oD:\out"], argv);
    }

    [Fact]
    public void attached_支持等号分隔()
    {
        var action = Action(
            new ManifestField
            {
                Id = "t", Label = "t", Type = "number",
                Style = FieldStyle.Attached, Prefix = "-mmt", Separator = "=",
            });

        var argv = ArgvBuilder.Build(action, Values(("t", 4)));

        Assert.Equal(["cmd", "-mmt=4"], argv);
    }

    [Fact]
    public void separate_把前缀和值拆成两个_token()
    {
        var action = Action(
            new ManifestField { Id = "o", Label = "o", Type = "text", Style = FieldStyle.Separate, Prefix = "--out" });

        var argv = ArgvBuilder.Build(action, Values(("o", "dir")));

        Assert.Equal(["cmd", "--out", "dir"], argv);
    }

    [Fact]
    public void literal_把所选选项的_args_原样展开()
    {
        var action = Action(
            new ManifestField
            {
                Id = "r", Label = "r", Type = "enum", Style = FieldStyle.Literal, SwitchBase = "-r",
                Values =
                [
                    new FieldValue { Value = "off", Label = "不递归", Args = ["-r-"] },
                    new FieldValue { Value = "on", Label = "递归", Args = ["-r"] },
                ],
            });

        Assert.Equal(["cmd", "-r-"], ArgvBuilder.Build(action, Values(("r", "off"))));
        Assert.Equal(["cmd", "-r"], ArgvBuilder.Build(action, Values(("r", "on"))));
    }

    [Fact]
    public void literal_的空_args_表示该选项不产生参数()
    {
        var action = Action(
            new ManifestField
            {
                Id = "mode", Label = "mode", Type = "enum", Style = FieldStyle.Literal,
                Values =
                [
                    new FieldValue { Value = "default", Label = "程序默认", Args = [] },
                    new FieldValue { Value = "on", Label = "开启", Args = ["-mhe"] },
                ],
            });

        Assert.Equal(["cmd"], ArgvBuilder.Build(action, Values(("mode", "default"))));
        Assert.Equal(["cmd", "-mhe"], ArgvBuilder.Build(action, Values(("mode", "on"))));
    }

    [Fact]
    public void literal_遇到选项外的值必须抛错_不能静默忽略()
    {
        var action = Action(
            new ManifestField
            {
                Id = "mode", Label = "mode", Type = "enum", Style = FieldStyle.Literal,
                Values = [new FieldValue { Value = "on", Label = "开", Args = ["-x"] }],
            });

        var ex = Assert.Throws<ManifestUsageException>(() => ArgvBuilder.Build(action, Values(("mode", "bogus"))));

        Assert.Contains("bogus", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void repeated_每个值各生成一个_token()
    {
        var action = Action(
            new ManifestField
            {
                Id = "ex", Label = "排除", Type = "multiselect", Style = FieldStyle.Repeated,
                Prefix = "-x!", Repeatable = true,
            });

        var argv = ArgvBuilder.Build(action, Values(("ex", new List<string> { "*.tmp", "node_modules" })));

        Assert.Equal(["cmd", "-x!*.tmp", "-x!node_modules"], argv);
    }

    [Fact]
    public void repeatable_的位置参数_每项一个_token()
    {
        var action = Action(
            new ManifestField
            {
                Id = "inputs", Label = "输入", Type = "paths", Style = FieldStyle.Positional,
                Repeatable = true,
            });

        var argv = ArgvBuilder.Build(action, Values(("inputs", new[] { "a.txt", "b.txt" })));

        Assert.Equal(["cmd", "a.txt", "b.txt"], argv);
    }

    // ---------------------------------------------------------------- perLine

    [Fact]
    public void positional_perLine_按行且按空白拆成多个_token()
    {
        var action = Action(
            new ManifestField
            {
                Id = "pairs", Label = "映射", Type = "textarea", Style = FieldStyle.Positional,
                PositionalMode = PositionalMode.PerLine,
            });

        var argv = ArgvBuilder.Build(action, Values(("pairs", "old.txt new.txt\r\n2.txt folder\\2new.txt\r\n")));

        Assert.Equal(["cmd", "old.txt", "new.txt", "2.txt", "folder\\2new.txt"], argv);
    }

    [Fact]
    public void positional_perLine_也能处理只用回车换行的文本()
    {
        // 重要：WinUI 的 TextBox 用 \r（而不是 \r\n）表示换行。
        // 只按 \n 拆分的话，界面上填的多个值会退化成一个参数——这是实测踩到的。
        var action = Action(
            new ManifestField
            {
                Id = "pairs", Label = "映射", Type = "textarea", Style = FieldStyle.Positional,
                PositionalMode = PositionalMode.PerLine,
            });

        var argv = ArgvBuilder.Build(action, Values(("pairs", "old.txt new.txt\r2.txt folder\\2new.txt")));

        Assert.Equal(["cmd", "old.txt", "new.txt", "2.txt", "folder\\2new.txt"], argv);
    }

    [Fact]
    public void positional_perLine_支持用双引号包住含空格的路径()
    {
        var action = Action(
            new ManifestField
            {
                Id = "pairs", Label = "映射", Type = "textarea", Style = FieldStyle.Positional,
                PositionalMode = PositionalMode.PerLine,
            });

        var argv = ArgvBuilder.Build(action, Values(("pairs", "\"My Docs/a.txt\" b.txt")));

        Assert.Equal(["cmd", "My Docs/a.txt", "b.txt"], argv);
    }

    // ---------------------------------------------------------------- 展示

    [Fact]
    public void 展示用的命令行会给含空格与引号的_token_加引号()
    {
        var display = ArgvBuilder.FormatForDisplay(@"C:\Program Files\7-Zip\7z.exe", ["x", @"D:\my docs\a.zip"]);

        Assert.Equal("\"C:\\Program Files\\7-Zip\\7z.exe\" x \"D:\\my docs\\a.zip\"", display);
    }


    [Fact]
    public void 冒号分隔符合成为一个_token()
    {
        // Windows 那批程序的 /Switch:Value 写法：规范现在允许 separator: ":"，
        // 宿主本来就是按 prefix + separator + 值 拼的（见 ArgvBuilder）。
        // 这条测试把它钉住，免得哪天真去掉了冒号又没人发现。
        var action = Action(
            new ManifestField { Id = "format", Label = "format", Type = "text", Style = FieldStyle.Attached, Prefix = "/Format", Separator = ":" });

        var argv = ArgvBuilder.Build(action, Values(("format", "Table")));

        Assert.Equal(["cmd", "/Format:Table"], argv);
    }
}
