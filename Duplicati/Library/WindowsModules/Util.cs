
// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.IO;

namespace Duplicati.Library.WindowsModules;

/// <summary>
///  Utility methods copied here to reduce dependencies from this module
/// </summary>
internal static class Util
{
    /// <summary>
    /// The path separator as a string
    /// </summary>
    private static readonly string DirectorySeparatorString = Path.DirectorySeparatorChar.ToString();

    /// <summary>
    /// Appends the appropriate directory separator to paths, depending on OS.
    /// Does not append the separator if the path already ends with it.
    /// </summary>
    /// <param name="path">The path to append to</param>
    /// <returns>The path with the directory separator appended</returns>
    public static string AppendDirSeparator(string path)
    {
        return AppendDirSeparator(path, DirectorySeparatorString);
    }

    /// <summary>
    /// Appends the specified directory separator to paths.
    /// Does not append the separator if the path already ends with it.
    /// </summary>
    /// <param name="path">The path to append to</param>
    /// <param name="separator">The directory separator to use</param>
    /// <returns>The path with the directory separator appended</returns>
    public static string AppendDirSeparator(string path, string separator)
    {
        return !path.EndsWith(separator, StringComparison.Ordinal) ? path + separator : path;
    }
}