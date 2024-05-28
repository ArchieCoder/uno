﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Uno.Extensions.Specialized;

namespace Windows.Storage
{
	internal class DataTypeSerializer
	{
		public readonly static Type[] SupportedTypes = new[]
		{
			typeof(bool),
			typeof(byte),
			typeof(char),
			typeof(char),
			typeof(DateTimeOffset),
			typeof(double),
			typeof(Guid),
			typeof(short),
			typeof(int),
			typeof(long),
			typeof(object),
			typeof(Foundation.Point),
			typeof(Foundation.Rect),
			typeof(float),
			typeof(Foundation.Size),
			typeof(string),
			typeof(TimeSpan),
			typeof(byte),
			typeof(ushort),
			typeof(uint),
			typeof(ulong),
			typeof(Uri),
			typeof(ApplicationDataCompositeValue)
		};

		public static object Deserialize(string value)
		{
			var index = value?.IndexOf(':') ?? -1;

			if (index != -1)
			{
				string typeName = value.Substring(0, index);
				var dataType = Type.GetType(typeName) ?? Type.GetType(typeName + ", " + typeof(Foundation.Point).GetTypeInfo().Assembly.FullName);
				var valueField = value.Substring(index + 1);

				if (dataType == typeof(DateTimeOffset))
				{
					return DateTimeOffset.Parse(valueField, CultureInfo.InvariantCulture);
				}
				else if (dataType == typeof(Guid))
				{
					return Guid.Parse(valueField);
				}
				else if (dataType == typeof(TimeSpan))
				{
					return TimeSpan.Parse(valueField, CultureInfo.InvariantCulture);
				}
				else if (dataType == typeof(ApplicationDataCompositeValue))
				{
					var data = JsonSerializer.Deserialize<Dictionary<string, object>>(valueField);
					return new ApplicationDataCompositeValue(data);
				}
				else
				{
					return Convert.ChangeType(valueField, dataType, CultureInfo.InvariantCulture);
				}
			}
			else
			{
				return null;
			}
		}

		public static string Serialize(object value)
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			var type = value.GetType();

			if (!SupportedTypes.Contains(type))
			{
				throw new NotSupportedException($"Type {value.GetType()} is not supported");
			}

			string serializedValue;
			if (type == typeof(ApplicationDataCompositeValue))
			{
				var composite = (ApplicationDataCompositeValue)value;
				var dictionary = composite.AsReadOnly();
				serializedValue = JsonSerializer.Serialize(dictionary);
			}
			else
			{
				serializedValue = Convert.ToString(value, CultureInfo.InvariantCulture);
			}

			return value.GetType().FullName + ":" + serializedValue;
		}
	}
}
