using System;
using System.Collections.Generic;
using System.Linq;

namespace SgmlReaderDll.Extensions {
  public static class Extensions {
    public static bool ContainsIgnoreCase( this IEnumerable<string> values, string value ) {
      return values != null && values.Any( v => v.EqualsIgnoreCase( value ) );
    }

    public static bool EqualsIgnoreCase( this string a, string b ) {
      return a.Equals( b, StringComparison.OrdinalIgnoreCase );
    }
  }
}
