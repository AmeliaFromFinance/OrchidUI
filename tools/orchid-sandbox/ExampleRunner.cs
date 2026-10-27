using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OrchidUI.Protocol;

namespace OrchidUI.Sandbox
{
    // Compiles an example action exactly as written and runs its Execute(). The only change is the
    // one Streamer.bot makes itself: CPHInline derives from a base that provides CPH and args.
    public static class ExampleRunner
    {
        public static readonly string[] Names = { "settings", "runtime", "minimal", "customised" };

        public static string FileFor(string examplesDir, string name)
        {
            var files = Directory.GetFiles(examplesDir, "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            int index = Array.IndexOf(Names, (name ?? "").ToLowerInvariant());
            if (index >= 0 && index < files.Length) return files[index];
            var direct = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
            if (direct != null) return direct;
            throw new FileNotFoundException("No example called '" + name + "' in " + examplesDir + ".");
        }

        public static Assembly Compile(string sourcePath)
        {
            var source = File.ReadAllText(sourcePath);
            var parse = new CSharpParseOptions(LanguageVersion.CSharp7_3);
            var tree = CSharpSyntaxTree.ParseText(source, parse, sourcePath);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.ValueText == "CPHInline");
            var withBase = cls.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName("global::OrchidUI.Sandbox.SandboxBase")))));
            root = root.ReplaceNode(cls, withBase);
            tree = CSharpSyntaxTree.Create(root, parse, sourcePath);

            var compilation = CSharpCompilation.Create(
                "Example_" + Guid.NewGuid().ToString("N"),
                new[] { tree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());
                    throw new InvalidOperationException("The example doesn't compile against this OrchidUI.dll:\n" + string.Join("\n", errors));
                }
                return Assembly.Load(ms.ToArray());
            }
        }

        private static IEnumerable<MetadataReference> References()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                typeof(Orchid).Assembly.Location,
                typeof(FakeCph).Assembly.Location,
                typeof(Uri).Assembly.Location,
            };
            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (tpa != null)
            {
                foreach (var p in tpa.Split(Path.PathSeparator))
                {
                    var n = Path.GetFileName(p);
                    if (n.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || n == "netstandard.dll" || n == "mscorlib.dll") paths.Add(p);
                }
            }
            else
            {
                var dir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                foreach (var n in new[] { "System.dll", "System.Core.dll", "System.Xml.dll" })
                {
                    var p = Path.Combine(dir, n);
                    if (File.Exists(p)) paths.Add(p);
                }
            }
            return paths.Where(File.Exists).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        }

        public static bool Run(Assembly asm, FakeCph cph)
        {
            var type = asm.GetType("CPHInline", true);
            var instance = (SandboxBase)Activator.CreateInstance(type);
            instance.CPH = cph;
            instance.args = cph.Args;
            var execute = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
            try { return (bool)execute.Invoke(instance, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // Runs the example and returns the spec it would send, without starting a host.
        internal static WindowSpec Dump(string sourcePath, FakeCph cph)
        {
            WindowSpec captured = null;
            Orchid.ShowHook = (o, spec) => captured = spec;
            try { Run(Compile(sourcePath), cph); }
            finally { Orchid.ShowHook = null; }
            return captured;
        }
    }
}
