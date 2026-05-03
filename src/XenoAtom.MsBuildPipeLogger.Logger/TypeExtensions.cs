// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Reflection;

namespace XenoAtom.MsBuildPipeLogger
{
    internal static class TypeExtensions
    {
        public static PropertyInfo? GetProperty(this Type type, string name) => type.GetTypeInfo().GetDeclaredProperty(name);

        public static MethodInfo? GetGetMethod(this PropertyInfo property) => property.GetMethod;

        public static FieldInfo? GetField(this Type type, string name) => type.GetTypeInfo().GetDeclaredField(name);
    }
}