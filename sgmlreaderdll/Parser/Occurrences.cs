using System.Collections.Generic;
using SgmlReaderDll.Parser.Enums;

namespace SgmlReaderDll.Parser {
  public class Occurrences {
    static Occurrences() {
      Values = new Dictionary<char, Occurrence> {
        {'?', Occurrence.Optional},
        {'+', Occurrence.OneOrMore},
        {'*', Occurrence.ZeroOrMore}
      };
    }

    private static readonly Dictionary<char, Occurrence> Values;

    public static Occurrence FromChar( char ch ) {
      return Values[ ch ];
    }

    public static bool Contains( char ch ) {
      return Values.ContainsKey( ch );
    }
  }
}
