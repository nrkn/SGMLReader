using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using SgmlReaderDll.Extensions;
using SgmlReaderDll.Parser;
using SgmlReaderDll.Reader.Enums;

namespace SgmlReaderDll.Reader {
  /// <summary>
  /// This class models an XML node, an array of elements in scope is maintained while parsing
  /// for validation purposes, and these Node objects are reused to reduce object allocation,
  /// hence the reset method.  
  /// </summary>
  internal class Node {
    internal XmlNodeType NodeType;
    internal string Value;
    internal XmlSpace Space;
    internal string XmlLang;
    internal bool IsEmpty;
    internal string Name;
    internal ElementDecl DtdType; // the DTD type found via validation
    internal State CurrentState;
    internal bool Simulated; // tag was injected into result stream.
    readonly HwStack _attributes = new HwStack( 10 );

    /// <summary>
    /// Attribute objects are reused during parsing to reduce memory allocations, 
    /// hence the Reset method. 
    /// </summary>
    public void Reset( string name, XmlNodeType nt, string value ) {
      Value = value;
      Name = name;
      NodeType = nt;
      Space = XmlSpace.None;
      XmlLang = null;
      IsEmpty = true;
      _attributes.Count = 0;
      DtdType = null;
    }

    public Attribute AddAttribute( string name, string value, char quotechar, bool caseInsensitive ) {
      Attribute a;
      // check for duplicates!
      var n = _attributes.Count;
      for( var i = 0; i < n; i++ ) {
        a = (Attribute) _attributes[ i ];
        if( a.Name.Equals( name, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal ) ) {
          return null;
        }
      }

      // This code makes use of the high water mark for attribute objects,
      // and reuses exisint Attribute objects to avoid memory allocation.
      a = (Attribute) _attributes.Push();
      if( a == null ) {
        a = new Attribute();
        _attributes[ _attributes.Count - 1 ] = a;
      }
      a.Reset( name, value, quotechar );

      return a;
    }

    [SuppressMessage( "Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage." )]
    public void RemoveAttribute( string name ) {
      var n = _attributes.Count;
      for( var i = 0; i < n; i++ ) {
        var a = (Attribute) _attributes[ i ];
        if( !a.Name.EqualsIgnoreCase( name ) ) continue;
        _attributes.RemoveAt( i );
        return;
      }
    }

    public void CopyAttributes( Node n ) {
      var len = n._attributes.Count;
      for( var i = 0; i < len; i++ ) {
        var a = (Attribute) n._attributes[ i ];
        var na = AddAttribute( a.Name, a.Value, a.QuoteChar, false );
        na.DtdType = a.DtdType;
      }
    }

    public int AttributeCount {
      get {
        return _attributes.Count;
      }
    }

    public int GetAttribute( string name ) {
      var n = _attributes.Count;
      for( var i = 0; i < n; i++ ) {
        var a = (Attribute) _attributes[ i ];
        if( a.Name.EqualsIgnoreCase( name ) ) {
          return i;
        }
      }
      return -1;
    }

    public Attribute GetAttribute( int i ) {
      if( i >= 0 && i < _attributes.Count ) {
        var a = (Attribute) _attributes[ i ];
        return a;
      }
      return null;
    }
  }
}