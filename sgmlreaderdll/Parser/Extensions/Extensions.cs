using System;
using System.Collections.Generic;
using System.Linq;

namespace SgmlReaderDll.Parser.Extensions {
  public static class Extensions {
    public static bool ContainsCaseInvariant( this IEnumerable<string> values, string value ) {
      return values != null && values.Any( v => v.Equals( value, StringComparison.OrdinalIgnoreCase ) );
    }
  }
}
