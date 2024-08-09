using System.Reflection;
using System.Runtime.Loader;

namespace UnrealSharp.Plugins
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly ICollection<string> _sharedAssemblies;
        private readonly AssemblyLoadContext _mainLoadContext;

        public string? AssemblyLoadedPath { get; private set; }

        public PluginLoadContext(string pluginPath, ICollection<string> sharedAssemblies, AssemblyLoadContext mainLoadContext, bool isCollectible) : base(isCollectible)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _sharedAssemblies = sharedAssemblies;
            _mainLoadContext = mainLoadContext;

            if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
            {
                return;
            }
            
            string? baseDirectory = Path.GetDirectoryName(pluginPath);
            
            if (baseDirectory != null)
            {
                if (!Path.EndsInDirectorySeparator(baseDirectory))
                {
                    baseDirectory += Path.DirectorySeparatorChar;
                }

                AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", baseDirectory);
            }
            else
            {
                Console.Error.WriteLine("Failed to set AppContext.BaseDirectory. Dynamic loading of libraries may fail.");
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null)
            {
                return null; 
            }
            
            if (_sharedAssemblies.Contains(assemblyName.Name))
            {
                return _mainLoadContext.LoadFromAssemblyName(assemblyName);
            }

            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath == null)
            {
                return default;
            }

            AssemblyLoadedPath = assemblyPath;
            
            using FileStream assemblyFile = File.Open(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

            if (!File.Exists(pdbPath))
            {
                return LoadFromStream(assemblyFile);
            }
            
            using var pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LoadFromStream(assemblyFile, pdbFile);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            
            return IntPtr.Zero;
        }
    }
}
