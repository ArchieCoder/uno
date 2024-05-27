#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Uno.UI.RemoteControl.HotReload.Messages
{
	[DebuggerDisplay("{Name}-{Scope}")]
	public class Frame
	{
		public Frame(short version, string scope, string name, string content)
		{
			Version = version;
			Scope = scope;
			Name = name;
			Content = content;
		}

		public int Version { get; }

		public string Scope { get; }

		public string Name { get; }

		public string Content { get; }

		public static Frame Read(Stream stream)
		{
			using (var reader = new BinaryReader(stream, Encoding.UTF8))
			{
				var version = reader.ReadInt16();
				var scope = reader.ReadString();
				var name = reader.ReadString();
				var content = reader.ReadString();

				return new Frame(version, scope, name, content);
			}
		}

		public static Frame Create<T>(short version, string scope, string name, T content)
			=> new Frame(
				version,
				scope,
				name,
				JsonConvert.SerializeObject(content)
			);

		public bool TryGetContent<T>([NotNullWhen(true)] out T? content)
		{
			try
			{
				content = JsonConvert.DeserializeObject<T>(Content);
				return content is not null;
			}
			catch (Exception)
			{
				content = default;
				return false;
			}
		}

		public void WriteTo(Stream stream)
		{
			var writer = new BinaryWriter(stream, Encoding.UTF8);

			writer.Write((short)Version);
			writer.Write(Scope);
			writer.Write(Name);
			writer.Write(Content);
		}
	}
}


namespace System.Diagnostics.CodeAnalysis
{
	/// <summary>
	/// Specifies that when a method returns <see cref="ReturnValue"/>, the parameter will not be null even if the corresponding type allows it.
	/// </summary>
	[global::System.AttributeUsage(global::System.AttributeTargets.Parameter, Inherited = false)]
	[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
	internal sealed class NotNullWhenAttribute : global::System.Attribute
	{
		/// <summary>
		/// Initializes the attribute with the specified return value condition.
		/// </summary>
		/// <param name="returnValue">The return value condition. If the method returns this value, the associated parameter will not be null.</param>
		public NotNullWhenAttribute(bool returnValue)
		{
			ReturnValue = returnValue;
		}

		/// <summary>Gets the return value condition.</summary>
		public bool ReturnValue { get; }
	}
}
