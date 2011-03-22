/*
 * 
 * An XmlReader implementation for loading SGML (including HTML) converting it
 * to well formed XML, by adding missing quotes, empty attribute values, ignoring
 * duplicate attributes, case folding on tag names, adding missing closing tags
 * based on SGML DTD information, and so on.
 *
 * Copyright (c) 2002 Microsoft Corporation. All rights reserved. (Chris Lovett)
 * 
 * Copyright (c) 2007-2011 MindTouch. All rights reserved.
 * 
 */

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using SgmlReaderDll.Extensions;
using SgmlReaderDll.Parser;
using SgmlReaderDll.Parser.Enums;
using SgmlReaderDll.Reader.Enums;

namespace SgmlReaderDll.Reader {
  /// <summary>
  /// SgmlReader is an XmlReader API over any SGML document (including built in 
  /// support for HTML).  
  /// </summary>
  public class SgmlReader : XmlReader {
    /// <summary>
    /// The value returned when a namespace is queried and none has been specified.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1705", Justification = "SgmlReader's standards for constants are different to Microsoft's and in line with older C++ style constants naming conventions.  Visually, constants using this style are more easily identifiable as such." )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1707", Justification = "SgmlReader's standards for constants are different to Microsoft's and in line with older C++ style constants naming conventions.  Visually, constants using this style are more easily identifiable as such." )]
    public const string UNDEFINED_NAMESPACE = "#unknown";

    private SgmlDtd _dtd;
    private Entity _current;
    private State _state;
    private char _partial;
    private string _endTag;
    private HwStack _stack;
    private Node _node; // current node (except for attributes)
    // Attributes are handled separately using these members.
    private Attribute _a;
    private int _apos; // which attribute are we positioned on in the collection.
    private Uri _baseUri;
    private StringBuilder _sb;
    private StringBuilder _name;
    private TextWriter _log;
    private bool _foundRoot;
    private bool _ignoreDtd;

    // autoclose support
    private Node _newnode;
    private int _poptodepth;
    private int _rootCount;

    private string _href;
    private string _errorLogFile;
    private Entity _lastError;
    private string _proxy;
    private TextReader _inputStream;
    private string _syslit;
    private string _pubid;
    private string _subset;
    private string _docType;
    private WhitespaceHandling _whitespaceHandling;
    private CaseFolding _folding = CaseFolding.None;
    private bool _stripDocType = true;
    //private string m_startTag;
    private readonly Dictionary<string, string> _unknownNamespaces = new Dictionary<string, string>();

    /// <summary>
    /// Initialises a new instance of the SgmlReader class.
    /// </summary>
    public SgmlReader() {
      Init();
    }

    /// <summary>
    /// Initialises a new instance of the SgmlReader class with an existing <see cref="XmlNameTable"/>, which is NOT used.
    /// </summary>
    /// <param name="nt">The nametable to use.</param>
    public SgmlReader( XmlNameTable nt ) {
      Init();
    }

    /// <summary>
    /// Specify the SgmlDtd object directly.  This allows you to cache the Dtd and share
    /// it across multipl SgmlReaders.  To load a DTD from a URL use the SystemLiteral property.
    /// </summary>
    public SgmlDtd Dtd {
      get {
        if( _dtd == null ) {
          LazyLoadDtd( _baseUri );
        }

        return _dtd;
      }
      set {
        _dtd = value;
      }
    }

    private void LazyLoadDtd( Uri baseUri ) {
      if( _dtd == null && !_ignoreDtd ) {
        if( string.IsNullOrEmpty( _syslit ) ) {
          if( _docType != null && _docType.EqualsIgnoreCase( "html" ) ) {
            var a = typeof( SgmlReader ).Assembly;
            var name = a.FullName.Split( ',' )[ 0 ] + ".Html.dtd";
            var stm = a.GetManifestResourceStream( name );
            if( stm != null ) {
              var sr = new StreamReader( stm );
              _dtd = SgmlDtd.Parse( baseUri, "HTML", sr, null, _proxy, null );
            }
          }
        }
        else {
          if( baseUri != null ) {
            baseUri = new Uri( baseUri, _syslit );
          }
          else if( _baseUri != null ) {
            baseUri = new Uri( _baseUri, _syslit );
          }
          else {
            baseUri = new Uri( new Uri( Directory.GetCurrentDirectory() + "\\" ), _syslit );
          }
          _dtd = SgmlDtd.Parse( baseUri, _docType, _pubid, baseUri.AbsoluteUri, _subset, _proxy, null );
        }
      }

      if( _dtd == null || _dtd.Name == null ) return;

      switch( CaseFolding ) {
        case CaseFolding.ToUpper:
          RootElementName = _dtd.Name.ToUpperInvariant();
          break;
        case CaseFolding.ToLower:
          RootElementName = _dtd.Name.ToLowerInvariant();
          break;
        default:
          RootElementName = _dtd.Name;
          break;
      }

      IsHtml = _dtd.Name.EqualsIgnoreCase( "html" );
    }

    /// <summary>
    /// The name of root element specified in the DOCTYPE tag.
    /// </summary>
    public string DocType {
      get {
        return _docType;
      }
      set {
        _docType = value;
      }
    }

    /// <summary>
    /// The root element of the document.
    /// </summary>
    public string RootElementName { get; private set; }

    /// <summary>
    /// The PUBLIC identifier in the DOCTYPE tag
    /// </summary>
    public string PublicIdentifier {
      get {
        return _pubid;
      }
      set {
        _pubid = value;
      }
    }

    /// <summary>
    /// The SYSTEM literal in the DOCTYPE tag identifying the location of the DTD.
    /// </summary>
    public string SystemLiteral {
      get {
        return _syslit;
      }
      set {
        _syslit = value;
      }
    }

    /// <summary>
    /// The DTD internal subset in the DOCTYPE tag
    /// </summary>
    public string InternalSubset {
      get {
        return _subset;
      }
      set {
        _subset = value;
      }
    }

    /// <summary>
    /// The input stream containing SGML data to parse.
    /// You must specify this property or the Href property before calling Read().
    /// </summary>
    public TextReader InputStream {
      get {
        return _inputStream;
      }
      set {
        _inputStream = value;
        Init();
      }
    }

    /// <summary>
    /// Sometimes you need to specify a proxy server in order to load data via HTTP
    /// from outside the firewall.  For example: "itgproxy:80".
    /// </summary>
    public string WebProxy {
      get {
        return _proxy;
      }
      set {
        _proxy = value;
      }
    }

    /// <summary>
    /// The base Uri is used to resolve relative Uri's like the SystemLiteral and
    /// Href properties.  This is a method because BaseURI is a read-only
    /// property on the base XmlReader class.
    /// </summary>
    public void SetBaseUri( string uri ) {
      _baseUri = new Uri( uri );
    }

    /// <summary>
    /// Specify the location of the input SGML document as a URL.
    /// </summary>
    public string Href {
      get {
        return _href;
      }
      set {
        _href = value;
        Init();
        if( _baseUri != null ) return;
        _baseUri = _href.IndexOf( "://" ) > 0 ? new Uri( _href ) : new Uri( "file:///" + Directory.GetCurrentDirectory() + "//" );
      }
    }

    /// <summary>
    /// Whether to strip out the DOCTYPE tag from the output (default true)
    /// </summary>
    public bool StripDocType {
      get {
        return _stripDocType;
      }
      set {
        _stripDocType = value;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to ignore any DTD reference.
    /// </summary>
    /// <value><c>true</c> if DTD references should be ignored; otherwise, <c>false</c>.</value>
    public bool IgnoreDtd {
      get { return _ignoreDtd; }
      set { _ignoreDtd = value; }
    }

    /// <summary>
    /// The case conversion behaviour while processing tags.
    /// </summary>
    public CaseFolding CaseFolding {
      get {
        return _folding;
      }
      set {
        _folding = value;
      }
    }

    /// <summary>
    /// DTD validation errors are written to this stream.
    /// </summary>
    public TextWriter ErrorLog {
      get {
        return _log;
      }
      set {
        _log = value;
      }
    }

    /// <summary>
    /// DTD validation errors are written to this log file.
    /// </summary>
    public string ErrorLogFile {
      get {
        return _errorLogFile;
      }
      set {
        _errorLogFile = value;
        _log = new StreamWriter( value );
      }
    }

    private void Log( string msg, params string[] args ) {
      if( ErrorLog == null ) return;
      var err = string.Format( CultureInfo.CurrentUICulture, msg, args );
      if( _lastError != _current ) {
        err = err + "    " + _current.Context();
        _lastError = _current;
        ErrorLog.WriteLine( "### Error:" + err );
      }
      else {
        var path = "";
        if( _current.ResolvedUri != null ) {
          path = _current.ResolvedUri.AbsolutePath;
        }

        ErrorLog.WriteLine( "### Error in {0}#{1}, line {2}, position {3}: {4}", path, _current.Name, _current.Line, _current.LinePosition, err );
      }
    }

    private void Log( string msg, char ch ) {
      Log( msg, ch.ToString() );
    }

    private void Init() {
      _state = State.Initial;
      _stack = new HwStack( 10 );
      _node = Push( null, XmlNodeType.Document, null );
      _node.IsEmpty = false;
      _sb = new StringBuilder();
      _name = new StringBuilder();
      _poptodepth = 0;
      _current = null;
      _partial = '\0';
      _endTag = null;
      _a = null;
      _apos = 0;
      _newnode = null;
      _rootCount = 0;
      _foundRoot = false;
      _unknownNamespaces.Clear();
    }

    private Node Push( string name, XmlNodeType nt, string value ) {
      var result = (Node) _stack.Push();
      if( result == null ) {
        result = new Node();
        _stack[ _stack.Count - 1 ] = result;
      }

      result.Reset( name, nt, value );
      _node = result;
      return result;
    }

    private void SwapTopNodes() {
      var top = _stack.Count - 1;
      if( top <= 0 ) return;
      var n = (Node) _stack[ top - 1 ];
      _stack[ top - 1 ] = _stack[ top ];
      _stack[ top ] = n;
    }

    private Node Push( Node n ) {
      // we have to do a deep clone of the Node object because
      // it is reused in the stack.
      var n2 = Push( n.Name, n.NodeType, n.Value );
      n2.DtdType = n.DtdType;
      n2.IsEmpty = n.IsEmpty;
      n2.Space = n.Space;
      n2.XmlLang = n.XmlLang;
      n2.CurrentState = n.CurrentState;
      n2.CopyAttributes( n );
      _node = n2;
      return n2;
    }

    private void Pop() {
      if( _stack.Count > 1 ) {
        _node = (Node) _stack.Pop();
      }
    }

    private Node Top() {
      var top = _stack.Count - 1;
      if( top > 0 ) {
        return (Node) _stack[ top ];
      }

      return null;
    }

    /// <summary>
    /// The node type of the node currently being parsed.
    /// </summary>
    public override XmlNodeType NodeType {
      get {
        switch( _state ) {
          case State.Attr:
            return XmlNodeType.Attribute;
          case State.AttrValue:
            return XmlNodeType.Text;
          case State.AutoClose:
          case State.EndTag:
            return XmlNodeType.EndElement;
        }

        return _node.NodeType;
      }
    }

    /// <summary>
    /// The name of the current node, if currently positioned on a node or attribute.
    /// </summary>
    public override string Name {
      get {
        string result = null;
        if( _state == State.Attr ) {
          result = XmlConvert.EncodeName( _a.Name );
        }
        else if( _state != State.AttrValue ) {
          result = _node.Name;
        }

        return result;
      }
    }

    /// <summary>
    /// The local name of the current node, if currently positioned on a node or attribute.
    /// </summary>
    public override string LocalName {
      get {
        var result = Name;
        if( result != null ) {
          var colon = result.IndexOf( ':' );
          if( colon != -1 ) {
            result = result.Substring( colon + 1 );
          }
        }
        return result;
      }
    }

    /// <summary>
    /// The namespace of the current node, if currently positioned on a node or attribute.
    /// </summary>
    /// <remarks>
    /// If not positioned on a node or attribute, <see cref="UNDEFINED_NAMESPACE"/> is returned.
    /// </remarks>
    [SuppressMessage( "Microsoft.Performance", "CA1820", Justification = "Cannot use IsNullOrEmpty in a switch statement and swapping the elegance of switch for a load of 'if's is not worth it." )]
    public override string NamespaceURI {
      get {
        // SGML has no namespaces, unless this turned out to be an xmlns attribute.
        if( _state == State.Attr && string.Equals( _a.Name, "xmlns", StringComparison.OrdinalIgnoreCase ) ) {
          return "http://www.w3.org/2000/xmlns/";
        }

        var prefix = Prefix;
        switch( Prefix ) {
          case "xmlns":
            return "http://www.w3.org/2000/xmlns/";
          case "xml":
            return "http://www.w3.org/XML/1998/namespace";
          case "":
            switch( NodeType ) {
              case XmlNodeType.Attribute:
                return string.Empty;
              case XmlNodeType.Element:
                for( var i = _stack.Count - 1; i > 0; --i ) {
                  var node = _stack[ i ] as Node;
                  if( ( node == null ) || ( node.NodeType != XmlNodeType.Element ) ) continue;

                  var index = node.GetAttribute( "xmlns" );
                  if( index < 0 ) continue;

                  var value = node.GetAttribute( index ).Value;
                  if( value != null ) {
                    return value;
                  }
                }
                break;
            }

            return string.Empty;
          default: {
              string value;
              if( ( NodeType == XmlNodeType.Attribute ) || ( NodeType == XmlNodeType.Element ) ) {

                // check if a 'xmlns:prefix' attribute is defined
                var key = "xmlns:" + prefix;
                for( var i = _stack.Count - 1; i > 0; --i ) {
                  var node = _stack[ i ] as Node;
                  if( ( node == null ) || ( node.NodeType != XmlNodeType.Element ) ) continue;

                  var index = node.GetAttribute( key );
                  if( index < 0 ) continue;

                  value = node.GetAttribute( index ).Value;
                  if( value != null ) {
                    return value;
                  }
                }
              }

              // check if we've seen this prefix before
              if( !_unknownNamespaces.TryGetValue( prefix, out value ) ) {
                if( _unknownNamespaces.Count > 0 ) {
                  value = UNDEFINED_NAMESPACE + _unknownNamespaces.Count;
                }
                else {
                  value = UNDEFINED_NAMESPACE;
                }
                _unknownNamespaces[ prefix ] = value;
              }
              return value;
            }
        }
      }
    }

    /// <summary>
    /// The prefix of the current node's name.
    /// </summary>
    public override string Prefix {
      get {
        var result = Name;
        if( result != null ) {
          var colon = result.IndexOf( ':' );
          result = colon != -1 ? result.Substring( 0, colon ) : string.Empty;
        }
        return result ?? string.Empty;
      }
    }

    /// <summary>
    /// Whether the current node has a value or not.
    /// </summary>
    public override bool HasValue {
      get {
        if( _state == State.Attr || _state == State.AttrValue ) {
          return true;
        }

        return ( _node.Value != null );
      }
    }

    /// <summary>
    /// The value of the current node.
    /// </summary>
    public override string Value {
      get {
        if( _state == State.Attr || _state == State.AttrValue ) {
          return _a.Value;
        }

        return _node.Value;
      }
    }

    /// <summary>
    /// Gets the depth of the current node in the XML document.
    /// </summary>
    /// <value>The depth of the current node in the XML document.</value>
    public override int Depth {
      get {
        switch( _state ) {
          case State.Attr:
            return _stack.Count;
          case State.AttrValue:
            return _stack.Count + 1;
        }

        return _stack.Count - 1;
      }
    }

    /// <summary>
    /// Gets the base URI of the current node.
    /// </summary>
    /// <value>The base URI of the current node.</value>
    public override string BaseURI {
      get {
        return _baseUri == null ? "" : _baseUri.AbsoluteUri;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the current node is an empty element (for example, &lt;MyElement/&gt;).
    /// </summary>
    public override bool IsEmptyElement {
      get {
        if( _state == State.Markup || _state == State.Attr || _state == State.AttrValue ) {
          return _node.IsEmpty;
        }

        return false;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the current node is an attribute that was generated from the default value defined in the DTD or schema.
    /// </summary>
    /// <value>
    /// true if the current node is an attribute whose value was generated from the default value defined in the DTD or
    /// schema; false if the attribute value was explicitly set.
    /// </value>
    public override bool IsDefault {
      get {
        if( _state == State.Attr || _state == State.AttrValue )
          return _a.IsDefault;

        return false;
      }
    }

    /// <summary>
    /// Gets the quotation mark character used to enclose the value of an attribute node.
    /// </summary>
    /// <value>The quotation mark character (" or ') used to enclose the value of an attribute node.</value>
    /// <remarks>
    /// This property applies only to an attribute node.
    /// </remarks>
    public override char QuoteChar {
      get {
        return _a != null ? _a.QuoteChar : '\0';
      }
    }

    /// <summary>
    /// Gets the current xml:space scope.
    /// </summary>
    /// <value>One of the <see cref="XmlSpace"/> values. If no xml:space scope exists, this property defaults to XmlSpace.None.</value>
    public override XmlSpace XmlSpace {
      get {
        for( var i = _stack.Count - 1; i > 1; i-- ) {
          var n = (Node) _stack[ i ];
          var xs = n.Space;
          if( xs != XmlSpace.None )
            return xs;
        }

        return XmlSpace.None;
      }
    }

    /// <summary>
    /// Gets the current xml:lang scope.
    /// </summary>
    /// <value>The current xml:lang scope.</value>
    public override string XmlLang {
      get {
        for( var i = _stack.Count - 1; i > 1; i-- ) {
          var n = (Node) _stack[ i ];
          var xmllang = n.XmlLang;
          if( xmllang != null )
            return xmllang;
        }

        return string.Empty;
      }
    }

    /// <summary>
    /// Specifies how white space is handled.
    /// </summary>
    public WhitespaceHandling WhitespaceHandling {
      get {
        return _whitespaceHandling;
      }
      set {
        _whitespaceHandling = value;
      }
    }

    /// <summary>
    /// Gets the number of attributes on the current node.
    /// </summary>
    /// <value>The number of attributes on the current node.</value>
    public override int AttributeCount {
      get {
        if( _state == State.Attr || _state == State.AttrValue )
          return 0;
        if( _node.NodeType == XmlNodeType.Element || _node.NodeType == XmlNodeType.DocumentType )
          return _node.AttributeCount;
        return 0;
      }
    }

    /// <summary>
    /// Gets the value of an attribute with the specified <see cref="Name"/>.
    /// </summary>
    /// <param name="name">The name of the attribute to retrieve.</param>
    /// <returns>The value of the specified attribute. If the attribute is not found, a null reference (Nothing in Visual Basic) is returned. </returns>
    public override string GetAttribute( string name ) {
      if( _state != State.Attr && _state != State.AttrValue ) {
        var i = _node.GetAttribute( name );
        if( i >= 0 )
          return GetAttribute( i );
      }

      return null;
    }

    /// <summary>
    /// Gets the value of the attribute with the specified <see cref="LocalName"/> and <see cref="NamespaceURI"/>.
    /// </summary>
    /// <param name="name">The local name of the attribute.</param>
    /// <param name="namespaceURI">The namespace URI of the attribute.</param>
    /// <returns>The value of the specified attribute. If the attribute is not found, a null reference (Nothing in Visual Basic) is returned. This method does not move the reader.</returns>
    public override string GetAttribute( string name, string namespaceURI ) {
      return GetAttribute( name ); // SGML has no namespaces.
    }

    /// <summary>
    /// Gets the value of the attribute with the specified index.
    /// </summary>
    /// <param name="i">The index of the attribute.</param>
    /// <returns>The value of the specified attribute. This method does not move the reader.</returns>
    public override string GetAttribute( int i ) {
      if( _state != State.Attr && _state != State.AttrValue ) {
        var a = _node.GetAttribute( i );
        if( a != null )
          return a.Value;
      }

      throw new ArgumentOutOfRangeException( "i" );
    }

    /// <summary>
    /// Gets the value of the attribute with the specified index.
    /// </summary>
    /// <param name="i">The index of the attribute.</param>
    /// <returns>The value of the specified attribute. This method does not move the reader.</returns>
    public override string this[ int i ] {
      get {
        return GetAttribute( i );
      }
    }

    /// <summary>
    /// Gets the value of an attribute with the specified <see cref="Name"/>.
    /// </summary>
    /// <param name="name">The name of the attribute to retrieve.</param>
    /// <returns>The value of the specified attribute. If the attribute is not found, a null reference (Nothing in Visual Basic) is returned. </returns>
    public override string this[ string name ] {
      get {
        return GetAttribute( name );
      }
    }

    /// <summary>
    /// Gets the value of the attribute with the specified <see cref="LocalName"/> and <see cref="NamespaceURI"/>.
    /// </summary>
    /// <param name="name">The local name of the attribute.</param>
    /// <param name="namespaceURI">The namespace URI of the attribute.</param>
    /// <returns>The value of the specified attribute. If the attribute is not found, a null reference (Nothing in Visual Basic) is returned. This method does not move the reader.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Design", "CA1023", Justification = "This design is that of Microsoft's XmlReader class and overriding its method is merely continuing the same design." )]
    public override string this[ string name, string namespaceURI ] {
      get {
        return GetAttribute( name, namespaceURI );
      }
    }

    /// <summary>
    /// Moves to the atttribute with the specified <see cref="Name"/>.
    /// </summary>
    /// <param name="name">The qualified name of the attribute.</param>
    /// <returns>true if the attribute is found; otherwise, false. If false, the reader's position does not change.</returns>
    public override bool MoveToAttribute( string name ) {
      var i = _node.GetAttribute( name );
      if( i >= 0 ) {
        MoveToAttribute( i );
        return true;
      }

      return false;
    }

    /// <summary>
    /// Moves to the attribute with the specified <see cref="LocalName"/> and <see cref="NamespaceURI"/>.
    /// </summary>
    /// <param name="name">The local name of the attribute.</param>
    /// <param name="ns">The namespace URI of the attribute.</param>
    /// <returns>true if the attribute is found; otherwise, false. If false, the reader's position does not change.</returns>
    public override bool MoveToAttribute( string name, string ns ) {
      return MoveToAttribute( name );
    }

    /// <summary>
    /// Moves to the attribute with the specified index.
    /// </summary>
    /// <param name="i">The index of the attribute to move to.</param>
    public override void MoveToAttribute( int i ) {
      var a = _node.GetAttribute( i );
      if( a != null ) {
        _apos = i;
        _a = a;
        if( _state != State.Attr ) {
          _node.CurrentState = _state; //save current state.
        }

        _state = State.Attr;
        return;
      }

      throw new ArgumentOutOfRangeException( "i" );
    }

    /// <summary>
    /// Moves to the first attribute.
    /// </summary>
    /// <returns></returns>
    public override bool MoveToFirstAttribute() {
      if( _node.AttributeCount > 0 ) {
        MoveToAttribute( 0 );
        return true;
      }

      return false;
    }

    /// <summary>
    /// Moves to the next attribute.
    /// </summary>
    /// <returns>true if there is a next attribute; false if there are no more attributes.</returns>
    /// <remarks>
    /// If the current node is an element node, this method is equivalent to <see cref="MoveToFirstAttribute"/>. If <see cref="MoveToNextAttribute"/> returns true,
    /// the reader moves to the next attribute; otherwise, the position of the reader does not change.
    /// </remarks>
    public override bool MoveToNextAttribute() {
      if( _state != State.Attr && _state != State.AttrValue ) {
        return MoveToFirstAttribute();
      }

      if( _apos < _node.AttributeCount - 1 ) {
        MoveToAttribute( _apos + 1 );
        return true;
      }

      return false;
    }

    /// <summary>
    /// Moves to the element that contains the current attribute node.
    /// </summary>
    /// <returns>
    /// true if the reader is positioned on an attribute (the reader moves to the element that owns the attribute); false if the reader is not positioned
    /// on an attribute (the position of the reader does not change).
    /// </returns>
    public override bool MoveToElement() {
      if( _state != State.Attr && _state != State.AttrValue )
        return ( _node.NodeType == XmlNodeType.Element );

      _state = _node.CurrentState;
      _a = null;

      return true;
    }

    /// <summary>
    /// Gets whether the content is HTML or not.
    /// </summary>
    public bool IsHtml { get; private set; }

    /// <summary>
    /// Returns the encoding of the current entity.
    /// </summary>
    /// <returns>The encoding of the current entity.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Design", "CA1024", Justification = "This method to get the encoding does not simply read a value, but potentially causes significant processing of the input stream." )]
    public Encoding GetEncoding() {
      if( _current == null ) {
        OpenInput();
      }

      return _current.Encoding;
    }

    private void OpenInput() {
      LazyLoadDtd( _baseUri );

      if( Href != null ) {
        _current = new Entity( "#document", null, _href, _proxy );
      }
      else if( _inputStream != null ) {
        _current = new Entity( "#document", null, _inputStream, _proxy );
      }
      else {
        throw new InvalidOperationException( "You must specify input either via Href or InputStream properties" );
      }

      _current.IsHtml = IsHtml;
      _current.Open( null, _baseUri );
      if( _current.ResolvedUri != null )
        _baseUri = _current.ResolvedUri;

      if( !_current.IsHtml || _dtd != null ) return;

      _docType = "HTML";
      LazyLoadDtd( _baseUri );
    }

    /// <summary>
    /// Reads the next node from the stream.
    /// </summary>
    /// <returns>true if the next node was read successfully; false if there are no more nodes to read.</returns>
    public override bool Read() {
      if( _current == null ) {
        OpenInput();
      }

      if( _node.Simulated ) {
        // return the next node
        _node.Simulated = false;
        _node = Top();
        _state = _node.CurrentState;
        return true;
      }

      var foundnode = false;
      while( !foundnode ) {
        switch( _state ) {
          case State.Initial:
            _state = State.Markup;
            _current.ReadChar();
            goto case State.Markup;
          case State.Eof:
            if( _current.Parent != null ) {
              _current.Close();
              _current = _current.Parent;
            }
            else {
              return false;
            }
            break;
          case State.EndTag:
            if( string.Equals( _endTag, _node.Name, StringComparison.OrdinalIgnoreCase ) ) {
              Pop(); // we're done!
              _state = State.Markup;
              goto case State.Markup;
            }
            Pop(); // close one element
            foundnode = true;// return another end element.
            break;
          case State.Markup:
            if( _node.IsEmpty ) {
              Pop();
            }
            foundnode = ParseMarkup();
            break;
          case State.PartialTag:
            Pop(); // remove text node.
            _state = State.Markup;
            foundnode = ParseTag( _partial );
            break;
          case State.PseudoStartTag:
            foundnode = ParseStartTag( '<' );
            break;
          case State.AutoClose:
            Pop(); // close next node.
            if( _stack.Count <= _poptodepth ) {
              _state = State.Markup;
              if( _newnode != null ) {
                Push( _newnode ); // now we're ready to start the new node.
                _newnode = null;
                _state = State.Markup;
              }
              else if( _node.NodeType == XmlNodeType.Document ) {
                _state = State.Eof;
                goto case State.Eof;
              }
            }
            foundnode = true;
            break;
          case State.CData:
            foundnode = ParseCData();
            break;
          case State.Attr:
            goto case State.AttrValue;
          case State.AttrValue:
            _state = State.Markup;
            goto case State.Markup;
          case State.Text:
            Pop();
            goto case State.Markup;
          case State.PartialText:
            if( ParseText( _current.Lastchar, false ) ) {
              _node.NodeType = XmlNodeType.Whitespace;
            }

            foundnode = true;
            break;
        }

        if( foundnode && _node.NodeType == XmlNodeType.Whitespace && _whitespaceHandling == WhitespaceHandling.None ) {
          // strip out whitespace (caller is probably pretty printing the XML).
          foundnode = false;
        }

        if( foundnode || _state != State.Eof || _stack.Count <= 1 ) continue;

        _poptodepth = 1;
        _state = State.AutoClose;
        _node = Top();
        return true;
      }
      if( !_foundRoot && ( NodeType == XmlNodeType.Element ||
              NodeType == XmlNodeType.Text ||
              NodeType == XmlNodeType.CDATA ) ) {
        _foundRoot = true;
        if( IsHtml && ( NodeType != XmlNodeType.Element ||
            !string.Equals( LocalName, "html", StringComparison.OrdinalIgnoreCase ) ) ) {
          // Simulate an HTML root element!
          _node.CurrentState = _state;
          var root = Push( "html", XmlNodeType.Element, null );
          SwapTopNodes(); // make html the outer element.
          _node = root;
          root.Simulated = true;
          root.IsEmpty = false;
          _state = State.Markup;
        }

        return true;
      }

      return true;
    }

    private bool ParseMarkup() {
      var ch = _current.Lastchar;
      if( ch == '<' ) {
        ch = _current.ReadChar();
        return ParseTag( ch );
      }

      if( ch != Entity.EOF ) {
        if( _node.DtdType != null && _node.DtdType.ContentModel.DeclaredContent == DeclaredContent.CDATA ) {
          // e.g. SCRIPT or STYLE tags which contain unparsed character data.
          _partial = '\0';
          _state = State.CData;
          return false;
        }

        if( ParseText( ch, true ) ) {
          _node.NodeType = XmlNodeType.Whitespace;
        }

        return true;
      }

      _state = State.Eof;
      return false;
    }

    private const string Declterm = " \t\r\n><";
    private bool ParseTag( char ch ) {
      switch( ch ) {
        case '%':
          return ParseAspNet();
        case '!':
          ch = _current.ReadChar();
          switch( ch ) {
            case '-':
              return ParseComment();
            case '[':
              return ParseConditionalBlock();
            default:
              if( ch != '_' && !char.IsLetter( ch ) ) {
                // perhaps it's one of those nasty office document hacks like '<![if ! ie ]>'
                var value = _current.ScanToEnd( _sb, "Recovering", ">" ); // skip it
                Log( "Ignoring invalid markup '<!" + value + ">" );
                return false;
              }
              var name = _current.ScanToken( _sb, Declterm, false );
              if( name.EqualsIgnoreCase( "DOCTYPE" ) ) {
                ParseDocType();

                // In SGML DOCTYPE SYSTEM attribute is optional, but in XML it is required,
                // therefore if there is no SYSTEM literal then add an empty one.
                if( GetAttribute( "SYSTEM" ) == null && GetAttribute( "PUBLIC" ) != null ) {
                  _node.AddAttribute( "SYSTEM", "", '"', _folding == CaseFolding.None );
                }

                if( _stripDocType ) {
                  return false;
                }

                _node.NodeType = XmlNodeType.DocumentType;
                return true;
              }

              Log( "Invalid declaration '<!{0}...'.  Expecting '<!DOCTYPE' only.", name );
              _current.ScanToEnd( null, "Recovering", ">" ); // skip it
              return false;
          }
        case '?':
          _current.ReadChar();// consume the '?' character.
          return ParsePi();
        case '/':
          return ParseEndTag();
        default:
          return ParseStartTag( ch );
      }
    }

    private string ScanName( string terminators ) {
      string name = _current.ScanToken( _sb, terminators, false );
      switch( _folding ) {
        case CaseFolding.ToUpper:
          name = name.ToUpperInvariant();
          break;
        case CaseFolding.ToLower:
          name = name.ToLowerInvariant();
          break;
      }
      return name;
    }

    private static bool VerifyName( string name ) {
      try {
        XmlConvert.VerifyName( name );
        return true;
      }
      catch( XmlException ) {
        return false;
      }
    }

    private const string Tagterm = " \t\r\n=/><";
    private const string Aterm = " \t\r\n='\"/>";
    private const string Avterm = " \t\r\n>";
    private bool ParseStartTag( char ch ) {
      string name = null;
      if( _state != State.PseudoStartTag ) {
        if( Tagterm.IndexOf( ch ) >= 0 ) {
          _sb.Length = 0;
          _sb.Append( '<' );
          _state = State.PartialText;
          return false;
        }

        name = ScanName( Tagterm );
      }
      else {
        // TODO: Changes by mindtouch mean that  startTag is never non-null.  The effects of this need checking.

        //name = startTag;
        _state = State.Markup;
      }

      var n = Push( name, XmlNodeType.Element, null );
      n.IsEmpty = false;
      Validate( n );
      ch = _current.SkipWhitespace();
      while( ch != Entity.EOF && ch != '>' ) {
        if( ch == '/' ) {
          n.IsEmpty = true;
          ch = _current.ReadChar();
          if( ch != '>' ) {
            Log( "Expected empty start tag '/>' sequence instead of '{0}'", ch );
            _current.ScanToEnd( null, "Recovering", ">" );
            return false;
          }
          break;
        }

        if( ch == '<' ) {
          Log( "Start tag '{0}' is missing '>'", name );
          break;
        }

        var aname = ScanName( Aterm );
        ch = _current.SkipWhitespace();
        if( new[] { ",", "=", ":", ";" }.ContainsIgnoreCase( aname ) ) continue;

        string value = null;
        char quote = '\0';
        if( ch == '=' || ch == '"' || ch == '\'' ) {
          if( ch == '=' ) {
            _current.ReadChar();
            ch = _current.SkipWhitespace();
          }

          if( ch == '\'' || ch == '\"' ) {
            quote = ch;
            value = ScanLiteral( _sb, ch );
          }
          else if( ch != '>' ) {
            value = _current.ScanToken( _sb, Avterm, false );
          }
        }

        if( ValidAttributeName( aname ) ) {
          var a = n.AddAttribute( aname, value ?? aname, quote, _folding == CaseFolding.None );
          if( a == null ) {
            Log( "Duplicate attribute '{0}' ignored", aname );
          }
          else {
            ValidateAttribute( n, a );
          }
        }

        ch = _current.SkipWhitespace();
      }

      if( ch == Entity.EOF ) {
        _current.Error( "Unexpected EOF parsing start tag '{0}'", name );
      }
      else if( ch == '>' ) {
        _current.ReadChar(); // consume '>'
      }

      if( Depth == 1 ) {
        if( _rootCount == 1 ) {
          // Hmmm, we found another root level tag, soooo, the only
          // thing we can do to keep this a valid XML document is stop
          _state = State.Eof;
          return false;
        }
        _rootCount++;
      }

      ValidateContent( n );
      return true;
    }

    private bool ParseEndTag() {
      _state = State.EndTag;
      _current.ReadChar(); // consume '/' char.
      var name = ScanName( Tagterm );
      var ch = _current.SkipWhitespace();
      if( ch != '>' ) {
        Log( "Expected empty start tag '/>' sequence instead of '{0}'", ch );
        _current.ScanToEnd( null, "Recovering", ">" );
      }

      _current.ReadChar(); // consume '>'

      _endTag = name;

      // Make sure there's a matching start tag for it.                        
      var caseInsensitive = ( _folding == CaseFolding.None );
      _node = (Node) _stack[ _stack.Count - 1 ];
      for( var i = _stack.Count - 1; i > 0; i-- ) {
        var n = (Node) _stack[ i ];
        if( !n.Name.Equals( name, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal ) ) continue;

        _endTag = n.Name;
        return true;
      }

      Log( "No matching start tag for '</{0}>'", name );
      _state = State.Markup;
      return false;
    }

    private bool ParseAspNet() {
      string value = "<%" + _current.ScanToEnd( _sb, "AspNet", "%>" ) + "%>";
      Push( null, XmlNodeType.CDATA, value );
      return true;
    }

    private bool ParseComment() {
      char ch = _current.ReadChar();
      if( ch != '-' ) {
        Log( "Expecting comment '<!--' but found {0}", ch );
        _current.ScanToEnd( null, "Comment", ">" );
        return false;
      }

      string value = _current.ScanToEnd( _sb, "Comment", "-->" );

      // Make sure it's a valid comment!
      int i = value.IndexOf( "--" );

      while( i >= 0 ) {
        int j = i + 2;
        while( j < value.Length && value[ j ] == '-' )
          j++;

        if( i > 0 ) {
          value = value.Substring( 0, i - 1 ) + "-" + value.Substring( j );
        }
        else {
          value = "-" + value.Substring( j );
        }

        i = value.IndexOf( "--" );
      }

      if( value.Length > 0 && value[ value.Length - 1 ] == '-' ) {
        value += " "; // '-' cannot be last character
      }

      Push( null, XmlNodeType.Comment, value );
      return true;
    }

    private const string Cdataterm = "\t\r\n[]<>";
    private bool ParseConditionalBlock() {
      _current.ReadChar(); // skip '['
      _current.SkipWhitespace();

      var name = _current.ScanToken( _sb, Cdataterm, false );
      if( name.StartsWith( "if " ) ) {
        // 'downlevel-revealed' comment (another atrocity of the IE team)
        _current.ScanToEnd( null, "CDATA", ">" );
        return false;
      }

      if( !string.Equals( name, "CDATA", StringComparison.OrdinalIgnoreCase ) ) {
        Log( "Expecting CDATA but found '{0}'", name );
        _current.ScanToEnd( null, "CDATA", ">" );
        return false;
      }

      var ch = _current.SkipWhitespace();
      if( ch != '[' ) {
        Log( "Expecting '[' but found '{0}'", ch );
        _current.ScanToEnd( null, "CDATA", ">" );
        return false;
      }

      var value = _current.ScanToEnd( _sb, "CDATA", "]]>" );

      Push( null, XmlNodeType.CDATA, value );
      return true;
    }

    private const string Dtterm = " \t\r\n>";
    private void ParseDocType() {
      _current.SkipWhitespace();
      var name = ScanName( Dtterm );
      Push( name, XmlNodeType.DocumentType, null );
      var ch = _current.SkipWhitespace();
      if( ch != '>' ) {
        var subset = "";
        var pubid = "";
        var syslit = "";

        if( ch != '[' ) {
          var token = _current.ScanToken( _sb, Dtterm, false );
          if( token.EqualsIgnoreCase( "PUBLIC" ) ) {
            ch = _current.SkipWhitespace();
            if( ch == '\"' || ch == '\'' ) {
              pubid = _current.ScanLiteral( _sb, ch );
              _node.AddAttribute( token, pubid, ch, _folding == CaseFolding.None );
            }
          }
          else if( !token.EqualsIgnoreCase( "SYSTEM" ) ) {
            Log( "Unexpected token in DOCTYPE '{0}'", token );
            _current.ScanToEnd( null, "DOCTYPE", ">" );
          }
          ch = _current.SkipWhitespace();
          if( ch == '\"' || ch == '\'' ) {
            token = "SYSTEM";
            syslit = _current.ScanLiteral( _sb, ch );
            _node.AddAttribute( token, syslit, ch, _folding == CaseFolding.None );
          }
          ch = _current.SkipWhitespace();
        }

        if( ch == '[' ) {
          subset = _current.ScanToEnd( _sb, "Internal Subset", "]" );
          _node.Value = subset;
        }

        ch = _current.SkipWhitespace();
        if( ch != '>' ) {
          Log( "Expecting end of DOCTYPE tag, but found '{0}'", ch );
          _current.ScanToEnd( null, "DOCTYPE", ">" );
        }

        if( _dtd != null && !_dtd.Name.EqualsIgnoreCase( name ) ) {
          throw new InvalidOperationException( "DTD does not match document type" );
        }

        _docType = name;
        _pubid = pubid;
        _syslit = syslit;
        _subset = subset;
        LazyLoadDtd( _current.ResolvedUri );
      }

      _current.ReadChar();
    }

    private const string Piterm = " \t\r\n?";
    private bool ParsePi() {
      var name = _current.ScanToken( _sb, Piterm, false );
      string value;
      if( _current.Lastchar != '?' ) {
        // Notice this is not "?>".  This is because Office generates bogus PI's that end with "/>".
        value = _current.ScanToEnd( _sb, "Processing Instruction", ">" );
        value = value.TrimEnd( '/' );
      }
      else {
        // error recovery.
        value = _current.ScanToEnd( _sb, "Processing Instruction", ">" );
      }

      // check if the name has a prefix; if so, ignore it
      var colon = name.IndexOf( ':' );
      if( colon > 0 ) {
        name = name.Substring( colon + 1 );
      }

      // skip xml declarations, since these are generated in the output instead.
      if( !name.EqualsIgnoreCase( "xml" ) ) {
        Push( name, XmlNodeType.ProcessingInstruction, value );
        return true;
      }

      return false;
    }

    private bool ParseText( char ch, bool newtext ) {
      var ws = !newtext || _current.IsWhitespace;
      if( newtext )
        _sb.Length = 0;

      //sb.Append(ch);
      //ch = current.ReadChar();
      _state = State.Text;
      while( ch != Entity.EOF ) {
        if( ch == '<' ) {
          ch = _current.ReadChar();
          if( ch == '/' || ch == '!' || ch == '?' || char.IsLetter( ch ) ) {
            // Hit a tag, so return XmlNodeType.Text token
            // and remember we partially started a new tag.
            _state = State.PartialTag;
            _partial = ch;
            break;
          }

          // not a tag, so just proceed.
          _sb.Append( '<' );
          _sb.Append( ch );
          ws = false;
          ch = _current.ReadChar();
        }
        else if( ch == '&' ) {
          ExpandEntity( _sb, '<' );
          ws = false;
          ch = _current.Lastchar;
        }
        else {
          if( !_current.IsWhitespace )
            ws = false;
          _sb.Append( ch );
          ch = _current.ReadChar();
        }
      }

      var value = _sb.ToString();
      Push( null, XmlNodeType.Text, value );
      return ws;
    }

    /// <summary>
    /// Consumes and returns a literal block of text, expanding entities as it does so.
    /// </summary>
    /// <param name="sb">The string builder to use.</param>
    /// <param name="quote">The delimiter for the literal.</param>
    /// <returns>The consumed literal.</returns>
    /// <remarks>
    /// This version is slightly different from <see cref="Entity.ScanLiteral"/> in that
    /// it also expands entities.
    /// </remarks>
    private string ScanLiteral( StringBuilder sb, char quote ) {
      sb.Length = 0;
      var ch = _current.ReadChar();
      while( ch != Entity.EOF && ch != quote && ch != '>' ) {
        if( ch == '&' ) {
          ExpandEntity( sb, quote );
          ch = _current.Lastchar;
        }
        else {
          sb.Append( ch );
          ch = _current.ReadChar();
        }
      }
      if( ch == quote ) {
        _current.ReadChar(); // consume end quote.
      }
      return sb.ToString();
    }

    private bool ParseCData() {
      // Like ParseText(), only it doesn't allow elements in the content.  
      // It allows comments and processing instructions and text only and
      // text is not returned as text but CDATA (since it may contain angle brackets).
      // And initial whitespace is ignored.  It terminates when we hit the
      // end tag for the current CDATA node (e.g. </style>).
      var ws = _current.IsWhitespace;
      _sb.Length = 0;
      var ch = _current.Lastchar;
      if( _partial != '\0' ) {
        Pop(); // pop the CDATA
        switch( _partial ) {
          case '!':
            _partial = ' '; // and pop the comment next time around
            return ParseComment();
          case '?':
            _partial = ' '; // and pop the PI next time around
            return ParsePi();
          case '/':
            _state = State.EndTag;
            return true;    // we are done!
          case ' ':
            break; // means we just needed to pop the Comment, PI or CDATA.
        }
      }

      // if partial == '!' then parse the comment and return
      // if partial == '?' then parse the processing instruction and return.            
      while( ch != Entity.EOF ) {
        if( ch == '<' ) {
          ch = _current.ReadChar();
          if( ch == '!' ) {
            ch = _current.ReadChar();
            if( ch == '-' ) {
              // return what CDATA we have accumulated so far
              // then parse the comment and return to here.
              if( ws ) {
                _partial = ' '; // pop comment next time through
                return ParseComment();
              }
              // return what we've accumulated so far then come
              // back in and parse the comment.
              _partial = '!';
              break;
            }

            // not a comment, so ignore it and continue on.
            _sb.Append( '<' );
            _sb.Append( '!' );
            _sb.Append( ch );
            ws = false;
          }
          else if( ch == '?' ) {
            // processing instruction.
            _current.ReadChar();// consume the '?' character.
            if( ws ) {
              _partial = ' '; // pop PI next time through
              return ParsePi();
            }

            _partial = '?';
            break;
          }
          else if( ch == '/' ) {
            // see if this is the end tag for this CDATA node.
            var temp = _sb.ToString();
            if( ParseEndTag() && string.Equals( _endTag, _node.Name, StringComparison.OrdinalIgnoreCase ) ) {
              if( ws || string.IsNullOrEmpty( temp ) ) {
                // we are done!
                return true;
              }

              // return CDATA text then the end tag
              _partial = '/';
              _sb.Length = 0; // restore buffer!
              _sb.Append( temp );
              _state = State.CData;
              break;
            }

            // wrong end tag, so continue on.
            _sb.Length = 0; // restore buffer!
            _sb.Append( temp );
            _sb.Append( "</" + _endTag + ">" );
            ws = false;

            // NOTE (steveb): we have one character in the buffer that we need to process next
            ch = _current.Lastchar;
            continue;
          }
          else {
            // must be just part of the CDATA block, so proceed.
            _sb.Append( '<' );
            _sb.Append( ch );
            ws = false;
          }
        }
        else {
          if( !_current.IsWhitespace && ws )
            ws = false;
          _sb.Append( ch );
        }

        ch = _current.ReadChar();
      }

      // NOTE (steveb): check if we reached EOF, which means it's over
      if( ch == Entity.EOF ) {
        _state = State.Eof;
        return false;
      }

      var value = _sb.ToString();

      // NOTE (steveb): replace any nested CDATA sections endings
      value = value.Replace( "<![CDATA[", string.Empty );
      value = value.Replace( "]]>", string.Empty );
      value = value.Replace( "/**/", string.Empty );

      Push( null, XmlNodeType.CDATA, value );
      if( _partial == '\0' )
        _partial = ' ';// force it to pop this CDATA next time in.

      return true;
    }

    private void ExpandEntity( StringBuilder sb, char terminator ) {
      var ch = _current.ReadChar();
      if( ch == '#' ) {
        var charent = _current.ExpandCharEntity();
        sb.Append( charent );
      }
      else {
        _name.Length = 0;
        while( ch != Entity.EOF &&
            ( char.IsLetter( ch ) || ch == '_' || ch == '-' ) || ( ( _name.Length > 0 ) && char.IsDigit( ch ) ) ) {
          _name.Append( ch );
          ch = _current.ReadChar();
        }
        var name = _name.ToString();
        if( _dtd != null && !string.IsNullOrEmpty( name ) ) {
          var e = _dtd.FindEntity( name );
          if( e != null ) {
            if( e.IsInternal ) {
              sb.Append( e.Literal );
              if( ch != terminator && ch != '&' && ch != Entity.EOF )
                _current.ReadChar();

              return;
            }
            
            var ex = new Entity( name, e.PublicId, e.Uri, _current.Proxy );
            e.Open( _current, new Uri( e.Uri ) );
            _current = ex;
            _current.ReadChar();
            return;
          }
          
          Log( "Undefined entity '{0}'", name );
        }
        // Entity is not defined, so just keep it in with the rest of the
        // text.
        sb.Append( "&" );
        sb.Append( name );
        if( ch != terminator && ch != '&' && ch != Entity.EOF ) {
          sb.Append( ch );
          _current.ReadChar();
        }
      }
    }

    /// <summary>
    /// Gets a value indicating whether the reader is positioned at the end of the stream.
    /// </summary>
    /// <value>true if the reader is positioned at the end of the stream; otherwise, false.</value>
    public override bool EOF {
      get {
        return _state == State.Eof;
      }
    }

    /// <summary>
    /// Changes the <see cref="ReadState"/> to Closed.
    /// </summary>
    public override void Close() {
      if( _current != null ) {
        _current.Close();
        _current = null;
      }

      if( _log == null ) return;

      _log.Close();
      _log = null;
    }

    /// <summary>
    /// Gets the state of the reader.
    /// </summary>
    /// <value>One of the ReadState values.</value>
    public override ReadState ReadState {
      get {
        switch( _state ) {
          case State.Initial:
            return ReadState.Initial;
          case State.Eof:
            return ReadState.EndOfFile;
          default:
            return ReadState.Interactive;
        }
      }
    }

    /// <summary>
    /// Reads the contents of an element or text node as a string.
    /// </summary>
    /// <returns>The contents of the element or an empty string.</returns>
    public override string ReadString() {
      if( _node.NodeType == XmlNodeType.Element ) {
        _sb.Length = 0;
        while( Read() ) {
          switch( NodeType ) {
            case XmlNodeType.CDATA:
            case XmlNodeType.SignificantWhitespace:
            case XmlNodeType.Whitespace:
            case XmlNodeType.Text:
              _sb.Append( _node.Value );
              break;
            default:
              return _sb.ToString();
          }
        }

        return _sb.ToString();
      }

      return _node.Value;
    }

    /// <summary>
    /// Reads all the content, including markup, as a string.
    /// </summary>
    /// <returns>
    /// All the XML content, including markup, in the current node. If the current node has no children,
    /// an empty string is returned. If the current node is neither an element nor attribute, an empty
    /// string is returned.
    /// </returns>
    public override string ReadInnerXml() {
      var sw = new StringWriter( CultureInfo.InvariantCulture );
      var xw = new XmlTextWriter( sw ) {
        Formatting = Formatting.Indented
      };

      switch( NodeType ) {
        case XmlNodeType.Element:
          Read();
          while( !EOF && NodeType != XmlNodeType.EndElement ) {
            xw.WriteNode( this, true );
          }
          Read(); // consume the end tag
          break;
        case XmlNodeType.Attribute:
          sw.Write( Value );
          break;
        default:
          // return empty string according to XmlReader spec.
          break;
      }

      xw.Close();
      return sw.ToString();
    }

    /// <summary>
    /// Reads the content, including markup, representing this node and all its children.
    /// </summary>
    /// <returns>
    /// If the reader is positioned on an element or an attribute node, this method returns all the XML content, including markup, of the current node and all its children; otherwise, it returns an empty string.
    /// </returns>
    public override string ReadOuterXml() {
      var sw = new StringWriter( CultureInfo.InvariantCulture );
      var xw = new XmlTextWriter( sw ) {
        Formatting = Formatting.Indented
      };
      xw.WriteNode( this, true );
      xw.Close();
      return sw.ToString();
    }

    /// <summary>
    /// Gets the XmlNameTable associated with this implementation.
    /// </summary>
    /// <value>The XmlNameTable enabling you to get the atomized version of a string within the node.</value>
    public override XmlNameTable NameTable {
      get {
        return null;
      }
    }

    /// <summary>
    /// Resolves a namespace prefix in the current element's scope.
    /// </summary>
    /// <param name="prefix">The prefix whose namespace URI you want to resolve. To match the default namespace, pass an empty string.</param>
    /// <returns>The namespace URI to which the prefix maps or a null reference (Nothing in Visual Basic) if no matching prefix is found.</returns>
    public override string LookupNamespace( string prefix ) {
      return null; // there are no namespaces in SGML.
    }

    /// <summary>
    /// Resolves the entity reference for EntityReference nodes.
    /// </summary>
    /// <exception cref="InvalidOperationException">SgmlReader does not resolve or return entities.</exception>
    public override void ResolveEntity() {
      // We never return any entity reference nodes, so this should never be called.
      throw new InvalidOperationException( "Not on an entity reference." );
    }

    /// <summary>
    /// Parses the attribute value into one or more Text, EntityReference, or EndEntity nodes.
    /// </summary>
    /// <returns>
    /// true if there are nodes to return. false if the reader is not positioned on an attribute node when the initial call is made or if all the
    /// attribute values have been read. An empty attribute, such as, misc="", returns true with a single node with a value of string.Empty.
    /// </returns>
    public override bool ReadAttributeValue() {
      switch( _state ) {
        case State.Attr:
          _state = State.AttrValue;
          return true;
        case State.AttrValue:
          return false;
        default:
          throw new InvalidOperationException( "Not on an attribute." );
      }
    }

    private void Validate( Node node ) {
      if( _dtd == null ) return;

      var e = _dtd.FindElement( node.Name );
      if( e == null ) return;

      node.DtdType = e;
      if( e.ContentModel.DeclaredContent == DeclaredContent.EMPTY )
        node.IsEmpty = true;
    }

    private static void ValidateAttribute( Node node, Attribute a ) {
      var e = node.DtdType;
      if( e == null ) return;

      var ad = e.FindAttribute( a.Name );
      if( ad != null ) {
        a.DtdType = ad;
      }
    }

    private static bool ValidAttributeName( string name ) {
      try {
        XmlConvert.VerifyNMTOKEN( name );
        var index = name.IndexOf( ':' );
        if( index >= 0 ) {
          XmlConvert.VerifyNCName( name.Substring( index + 1 ) );
        }

        return true;
      }
      catch( XmlException ) {
        return false;
      }
      catch( ArgumentNullException ) {
        // (steveb) this is probably a bug in XmlConvert.VerifyNCName when passing in an empty string
        return false;
      }
    }

    private void ValidateContent( Node node ) {
      if( node.NodeType == XmlNodeType.Element ) {
        if( !VerifyName( node.Name ) ) {
          Pop();
          Push( null, XmlNodeType.Text, "<" + node.Name + ">" );
          return;
        }
      }

      if( _dtd == null ) return;

      // See if this element is allowed inside the current element.
      // If it isn't, then auto-close elements until we find one
      // that it is allowed to be in.                                  
      var name = node.Name.ToUpperInvariant(); // DTD is in upper case
      var i = 0;
      var top = _stack.Count - 2;
      if( node.DtdType != null ) {
        // it is a known element, let's see if it's allowed in the
        // current context.
        for( i = top; i > 0; i-- ) {
          var n = (Node) _stack[ i ];
          if( n.IsEmpty )
            continue; // we'll have to pop this one
          var f = n.DtdType;
          if(
            // Since we don't understand this tag anyway,
            // we might as well allow this content!
            f == null ||
            // NOTE (steveb): never close the BODY tag too early
            ( i == 2 && f.Name.EqualsIgnoreCase( "BODY" ) ) ||
            // can't pop the root element.
            f.Name.EqualsIgnoreCase( _dtd.Name ) ||
            f.CanContain( name, _dtd ) ||
            // If the end tag is not optional then we can't
            // auto-close it.  We'll just have to live with the
            // junk we've found and move on.            
            !f.EndTagOptional
          ) break;          
        }
      }

      if( i == 0 ) {
        // Tag was not found or is not allowed anywhere, ignore it and 
        // continue on.
        return;
      }

      if( i >= top ) return;
      var t = (Node) _stack[ top ];
      if( i == top - 1 && string.Equals( name, t.Name, StringComparison.OrdinalIgnoreCase ) ) {
        // e.g. p not allowed inside p, not an interesting error.
      }
      else {
#if DEBUG
        var closing = "";
        for( var k = top; k >= i + 1; k-- ) {
          if( closing != "" ) closing += ",";
          var n2 = (Node) _stack[ k ];
          closing += "<" + n2.Name + ">";
        }
        Log( "Element '{0}' not allowed inside '{1}', closing {2}.", name, t.Name, closing );
#endif
      }

      _state = State.AutoClose;
      _newnode = node;
      Pop(); // save this new node until we pop the others
      _poptodepth = i + 1;
    }
  }
}