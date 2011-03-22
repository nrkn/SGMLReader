using SgmlReaderDll.Parser;

namespace SgmlReaderDll.Reader {
  /// <summary>
  /// This class represents an attribute.  The AttDef is assigned
  /// from a validation process, and is used to provide default values.
  /// </summary>
  internal class Attribute {
    internal string Name;    // the atomized name.
    internal AttDef DtdType; // the AttDef of the attribute from the SGML DTD.
    internal char QuoteChar; // the quote character used for the attribute value.
    private string _literalValue; // the attribute value

    /// <summary>
    /// Attribute objects are reused during parsing to reduce memory allocations, 
    /// hence the Reset method.
    /// </summary>
    public void Reset( string name, string value, char quote ) {
      Name = name;
      _literalValue = value;
      QuoteChar = quote;
      DtdType = null;
    }

    public string Value {
      get {
        if( _literalValue != null )
          return _literalValue;
        return DtdType != null ? DtdType.Default : null;
      }
    }

    public bool IsDefault {
      get {
        return _literalValue == null;
      }
    }
  }
}