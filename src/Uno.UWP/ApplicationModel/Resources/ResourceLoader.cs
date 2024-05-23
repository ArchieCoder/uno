#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Uno;
using Uno.Extensions;
using Uno.Foundation.Logging;
using Uno.UI;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.Globalization;

namespace Windows.ApplicationModel.Resources;

public sealed partial class ResourceLoader // contract
{
	public static ResourceLoader GetForCurrentView() => GetOrCreateNamedResourceLoader(DefaultResourceLoaderName);

	public static ResourceLoader GetForCurrentView(string name) => GetOrCreateNamedResourceLoader(name);

	public static ResourceLoader GetForViewIndependentUse() => GetOrCreateNamedResourceLoader(DefaultResourceLoaderName);

	public static ResourceLoader GetForViewIndependentUse(string name) => GetOrCreateNamedResourceLoader(name);

	[NotImplemented]
	public string GetStringForUri(Uri uri) => throw new NotSupportedException();

	[NotImplemented]
	public static string GetStringForReference(Uri uri) => throw new NotSupportedException();
}

partial class ResourceLoader // static
{
	#region public static string DefaultLanguage
	/// <summary>
	/// Provides the default culture if CurrentUICulture cannot provide it.
	/// </summary>
	public static string? DefaultLanguage
	{
		get => _defaultLanguage;
		set
		{
			_defaultLanguage = value;

#if __WASM__
			if (CultureInfo.CurrentUICulture.IetfLanguageTag.Length == 0 &&  // is not invariant-culture
				!string.IsNullOrEmpty(value))
			{
				CultureInfo.CurrentCulture = new CultureInfo(value);
				CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
			}
#endif

			EnsureLoadersCultures();
		}
	}

	private static string? _defaultLanguage;
	#endregion

	private const string DefaultResourceLoaderName = "Resources";
	private static readonly Logger _log = typeof(ResourceLoader).Log();

	private static readonly List<Assembly> _lookupAssemblies = new();
	private static readonly HashSet<(Assembly Assembly, string ResourceName)> _parsedResources = new();
	private static readonly Dictionary<string, ResourceLoader> _loaders = new(StringComparer.OrdinalIgnoreCase); // _loaders[RES_PACK ?? "Resources"]._resources[CULTURE][RES_KEY]
	private static LoaderContext? _loaderContext;

	private static string[] EnsureLoadersCultures()
	{
		if (HasContextChangedSignificantly(out var context))
		{
			ReloadResources(context);
		}

		return context.LanguagePreferences;
	}

	public static void AddLookupAssembly(Assembly assembly)
	{
		_lookupAssemblies.Add(assembly);

		if (HasContextChangedSignificantly(out var context))
		{
			// The cache is still valid, we only have to load resources from the given assembly
			ProcessAssembly(assembly, context.LanguagePreferences);
		}
		else
		{
			// The current culture was altered, rebuild the whole/missing cache
			ReloadResources(context);
		}
	}

	private static void ProcessAssembly(Assembly assembly, string[] languagePreferences)
	{
		var resourceNames = assembly.GetManifestResourceNames();
		foreach (var name in resourceNames)
		{
			if (name.EndsWith(".upri", StringComparison.Ordinal))
			{
				ProcessResourceFile(assembly, name, assembly.GetManifestResourceStream(name), languagePreferences);
			}
		}
	}

	private static void ReloadResources(LoaderContext context)
	{
		if (!WinRTFeatureConfiguration.ResourceLoader.PreserveParsedResources)
		{
			ClearResources();
			_parsedResources.Clear();
		}

		foreach (var assembly in _lookupAssemblies)
		{
			ProcessAssembly(assembly, context.LanguagePreferences);
		}

		_loaderContext = context;
	}

	private static void ClearResources()
	{
		// We clear each loader independently instead of clearing the '_loaders'
		// so if a loader instance has been captured, it will be updated
		foreach (var loader in _loaders.Values)
		{
			loader._resources.Clear();
		}
	}

	private static bool HasContextChangedSignificantly(out LoaderContext context)
	{
		var capture = new LoaderContext(
			WinRTFeatureConfiguration.ResourceLoader.UsePrimaryLanguageOverride,
			ApplicationLanguages.PrimaryLanguageOverride,
			CultureInfo.CurrentUICulture,
			DefaultLanguage,
			default!);
		if (_loaderContext is null ||
			capture != _loaderContext with { LanguagePreferences = default! })
		{
			var preferences = GetLanguagePreferences(capture);
			if (_loaderContext is null ||
				!_loaderContext.LanguagePreferences.SequenceEqual(preferences))
			{
				_log.Trace($"HasContextChangedSignificantly: true");
				context = capture with { LanguagePreferences = preferences };
				return true;
			}
		}

		context = _loaderContext;
		return false;
	}

	private static string[] GetLanguagePreferences(LoaderContext context)
	{
		var plo = WinRTFeatureConfiguration.ResourceLoader.UsePrimaryLanguageOverride
				? context.PLO
				// invariant culture doesn't have an IetfLanguageTag, and will be discarded below
				: context.UICulture?.IetfLanguageTag;
		return (ApplicationLanguages.Languages ?? Array.Empty<string>())
			.Prepend(plo)
			.Append(context.DefaultLanguage)
			.Distinct()
			.OrderBy(x => string.IsNullOrEmpty(plo) ? false : !FastBaseCultureComparer.Instance.Equals(x, plo))
			.Where(x => !string.IsNullOrEmpty(x))
			.OfType<string>()
			.ToArray();
	}

	private static void ProcessResourceFile(Assembly assembly, string fileName, Stream? stream, string[] languagePreferences)
	{
		if (stream is null)
		{
			throw new Exception($"The resource file {fileName} could not be read.");
		}

		using (var reader = new BinaryReader(stream))
		{
			// "Magic" sequence to ensure we're reading a proper resource file
			var magic = reader.ReadBytes(3);
			if (!magic.SequenceEqual(new byte[] { 0x75, 0x6E, 0x6F })) // == "uno"
			{
				throw new InvalidOperationException($"The file {fileName} is not a resource file");
			}

			var version = reader.ReadInt32();
			if (version is not (3 or 2))
			{
				throw new InvalidOperationException($"The resource file {fileName} has an invalid version (got {version}, expecting 2 or 3)");
			}

			var name = reader.ReadString();
			var culture = reader.ReadString().ToLowerInvariant();

			if (!languagePreferences.Contains(culture, FastBaseCultureComparer.Instance))
			{
				// Currently only load the resources for the current culture.
				if (_log.IsEnabled(LogLevel.Debug))
				{
					_log.LogDebug($"Skipping resource file {fileName} for {culture} (preferences: {string.Join(",", languagePreferences)})");
				}
				return;
			}
			if (!_parsedResources.Add((assembly, fileName/* keyed by fileName, not name */)))
			{
				if (_log.IsEnabled(LogLevel.Debug))
				{
					_log.LogDebug($"Skipping already parsed resource file {fileName} for {culture}");
				}
				return;
			}

			var loader = GetOrCreateNamedResourceLoader(name);
			if (!loader._resources.TryGetValue(culture, out var resources))
			{
				loader._resources[culture] = resources = new Dictionary<string, string>();
			}

			var resourceCount = reader.ReadInt32();
			StringBuilder sb = new();
			for (var i = 0; i < resourceCount; i++)
			{
				var key = reader.ReadString();
				var value = reader.ReadString();

				if (version == 2)
				{
					// Restore the original format
					key = key.Replace("/", ".");

					var firstDotIndex = key.IndexOf('.');
					if (firstDotIndex != -1)
					{
						sb.Clear();
						sb.Append(key);

						sb[firstDotIndex] = '/';

						key = sb.ToString();
					}
				}

				if (_log.IsEnabled(LogLevel.Debug))
				{
					_log.LogDebug($"[{name}, {culture}, {fileName}] Adding resource: {key}={value}");
				}

				resources[key] = value;
			}
		}
	}

	private static ResourceLoader GetOrCreateNamedResourceLoader(string name) =>
		_loaders.FindOrCreate(name, () => new ResourceLoader(name, addLoader: false));

	private record class LoaderContext(bool UsePrimaryLanguageOverride, string? PLO, CultureInfo? UICulture, string? DefaultLanguage, string[] LanguagePreferences);

	/// <summary>
	/// Allows for case-insensitive culture comparison using base culture: FR == fr-CA
	/// </summary>
	private class FastBaseCultureComparer : EqualityComparer<string>
	{
		public static FastBaseCultureComparer Instance { get; } = new();
		public static string GetBaseCulture(string value) => value.Split('-', 2)[0].ToLowerInvariant();

		public override int GetHashCode([DisallowNull] string x) => x.Split('-', 2)[0].ToLowerInvariant().GetHashCode();
		public override bool Equals(string? c1, string? c2)
		{
			return
				c1 is { } &&
				c2 is { } &&
				GetBaseCulture(c1) == GetBaseCulture(c2);
		}
	}
}

partial class ResourceLoader // instanced
{
	private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase); // _resources[CULTURE][RES_KEY] => RES_VALUE

	internal string LoaderName { get; }

	public ResourceLoader() : this(DefaultResourceLoaderName, true)
	{
	}

	public ResourceLoader(string name) : this(name, true)
	{
	}

	/// <summary>
	/// Creates a loader with a given name.
	/// If the loader does not exist yet, it can add it if requested.
	/// </summary>
	/// <param name="name">Name of the loader.</param>
	/// <param name="addLoader">
	/// A value indicating whether the loader
	/// should be added to the list of loaders.
	/// </param>
	private ResourceLoader(string name, bool addLoader)
	{
		if (_log.IsEnabled(LogLevel.Debug))
		{
			_log.LogDebug($"Initializing ResourceLoader[\"{name}\"]");
		}

		LoaderName = name;

		if (_loaders.TryGetValue(name, out var existingLoader))
		{
			// If there is already a loader with the same name,
			// they should share the same resources.
			_resources = existingLoader._resources;
		}
		else if (addLoader)
		{
			_loaders[name] = this;
		}
	}

	public string? GetString(string resource)
	{
		// "/[file]/[name]" format support
		if (resource.ElementAtOrDefault(0) == '/')
		{
			var separatorIndex = resource.IndexOf('/', 1);
			if (separatorIndex < 1)
			{
				return "";
			}
			var resourceFile = resource.Substring(1, separatorIndex - 1);
			var resourceName = resource.Substring(separatorIndex + 1);
			return GetForCurrentView(resourceFile).GetString(resourceName);
		}

		// First make sure that resource cache matches the current culture
		var cultures = EnsureLoadersCultures();

		// Walk the culture hierarchy and the default
		foreach (var culture in cultures)
		{
			if (FindForCulture(culture, resource, out var value))
			{
				return value;
			}
		}

		return string.Empty;
	}

	private bool FindForCulture(string culture, string resource, out string? resourceValue)
	{
		if (_log.IsEnabled(LogLevel.Debug))
		{
			_log.LogDebug($"[{LoaderName}] FindForCulture {culture}, {resource}");
		}

		if (_resources.TryGetValue(culture, out var map1) &&
			map1.TryGetValue(resource, out resourceValue))
		{
			return true;
		}

		// if we cant find using the specific culture, fallback on base culture, and then sibling cultures
		if (culture.Split('-', 2)[0] is { Length: > 0 } baseCulture)
		{
			var relatedCultures = _resources.Keys
				.Where(x => x.StartsWith(baseCulture, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(x => x == baseCulture) // base culture first
				.ThenByDescending(x => x); // and then, sibling cultures in reverse order (from ex-ZZ to ex-AA)
			foreach (var related in relatedCultures)
			{
				if (_resources.TryGetValue(related, out var map2) &&
					map2.TryGetValue(resource, out resourceValue))
				{
					return true;
				}
			}
		}

		resourceValue = null;
		return false;
	}
}
