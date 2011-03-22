/*
 * 
 * Copyright (c) 2007-2011 MindTouch. All rights reserved.
 * 
 */

using System;

namespace SgmlReaderDll.Parser {
  internal static class StringUtilities
    {
        public static bool EqualsIgnoreCase(string a, string b){
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
