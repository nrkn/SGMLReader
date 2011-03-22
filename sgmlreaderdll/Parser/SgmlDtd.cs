using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using SgmlReaderDll.Extensions;
using SgmlReaderDll.Parser.Enums;

namespace SgmlReaderDll.Parser {
  /// <summary>
  /// Provides DTD parsing and support for the SgmlParser framework.
  /// </summary>
  public class SgmlDtd {
    private readonly Dictionary<string, ElementDecl> _elements;
    private readonly Dictionary<string, Entity> _pentities;
    private readonly Dictionary<string, Entity> _entities;
    private readonly StringBuilder _sb;
    private Entity _current;

    /// <summary>
    /// Initialises a new instance of the <see cref="SgmlDtd"/> class.
    /// </summary>
    /// <param name="name">The name of the DTD.</param>
    /// <param name="nt">The <see cref="XmlNameTable"/> is NOT used.</param>
    public SgmlDtd( string name, XmlNameTable nt ) {
      Name = name;
      _elements = new Dictionary<string, ElementDecl>();
      _pentities = new Dictionary<string, Entity>();
      _entities = new Dictionary<string, Entity>();
      _sb = new StringBuilder();
    }

    /// <summary>
    /// The name of the DTD.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the XmlNameTable associated with this implementation.
    /// </summary>
    /// <value>The XmlNameTable enabling you to get the atomized version of a string within the node.</value>
    public XmlNameTable NameTable {
      get {
        return null;
      }
    }

    /// <summary>
    /// Parses a DTD and creates a <see cref="SgmlDtd"/> instance that encapsulates the DTD.
    /// </summary>
    /// <param name="baseUri">The base URI of the DTD.</param>
    /// <param name="name">The name of the DTD.</param>
    /// <param name="pubid"></param>
    /// <param name="url"></param>
    /// <param name="subset"></param>
    /// <param name="proxy"></param>
    /// <param name="nt">The <see cref="XmlNameTable"/> is NOT used.</param>
    /// <returns>A new <see cref="SgmlDtd"/> instance that encapsulates the DTD.</returns>
    public static SgmlDtd Parse( Uri baseUri, string name, string pubid, string url, string subset, string proxy, XmlNameTable nt ) {
      var dtd = new SgmlDtd( name, nt );
      if( !string.IsNullOrEmpty( url ) ) {
        dtd.PushEntity( baseUri, new Entity( dtd.Name, pubid, url, proxy ) );
      }

      if( !string.IsNullOrEmpty( subset ) ) {
        dtd.PushEntity( baseUri, new Entity( name, subset ) );
      }

      try {
        dtd.Parse();
      }
      catch( ApplicationException e ) {
        throw new SgmlParseException( e.Message + dtd._current.Context() );
      }

      return dtd;
    }

    /// <summary>
    /// Parses a DTD and creates a <see cref="SgmlDtd"/> instance that encapsulates the DTD.
    /// </summary>
    /// <param name="baseUri">The base URI of the DTD.</param>
    /// <param name="name">The name of the DTD.</param>
    /// <param name="input">The reader to load the DTD from.</param>
    /// <param name="subset"></param>
    /// <param name="proxy">The proxy server to use when loading resources.</param>
    /// <param name="nt">The <see cref="XmlNameTable"/> is NOT used.</param>
    /// <returns>A new <see cref="SgmlDtd"/> instance that encapsulates the DTD.</returns>
    [SuppressMessage( "Microsoft.Reliability", "CA2000", Justification = "The entities created here are not temporary and should not be disposed here." )]
    public static SgmlDtd Parse( Uri baseUri, string name, TextReader input, string subset, string proxy, XmlNameTable nt ) {
      var dtd = new SgmlDtd( name, nt );
      dtd.PushEntity( baseUri, new Entity( dtd.Name, baseUri, input, proxy ) );
      if( !string.IsNullOrEmpty( subset ) ) {
        dtd.PushEntity( baseUri, new Entity( name, subset ) );
      }

      try {
        dtd.Parse();
      }
      catch( ApplicationException e ) {
        throw new SgmlParseException( e.Message + dtd._current.Context() );
      }

      return dtd;
    }

    /// <summary>
    /// Finds an entity in the DTD with the specified name.
    /// </summary>
    /// <param name="name">The name of the <see cref="Entity"/> to find.</param>
    /// <returns>The specified Entity from the DTD.</returns>
    public Entity FindEntity( string name ) {
      Entity e;
      _entities.TryGetValue( name, out e );
      return e;
    }

    /// <summary>
    /// Finds an element declaration in the DTD with the specified name.
    /// </summary>
    /// <param name="name">The name of the <see cref="ElementDecl"/> to find and return.</param>
    /// <returns>The <see cref="ElementDecl"/> matching the specified name.</returns>
    public ElementDecl FindElement( string name ) {
      ElementDecl el;
      _elements.TryGetValue( name.ToUpperInvariant(), out el );
      return el;
    }

    //-------------------------------- Parser -------------------------
    private void PushEntity( Uri baseUri, Entity e ) {
      e.Open( _current, baseUri );
      _current = e;
      _current.ReadChar();
    }

    private void PopEntity() {
      if( _current != null ) _current.Close();
      _current = _current.Parent;
    }

    private void Parse() {
      var ch = _current.Lastchar;
      while( true ) {
        switch( ch ) {
          case Entity.EOF:
            PopEntity();
            if( _current == null )
              return;
            ch = _current.Lastchar;
            break;
          case ' ':
          case '\n':
          case '\r':
          case '\t':
            ch = _current.ReadChar();
            break;
          case '<':
            ParseMarkup();
            ch = _current.ReadChar();
            break;
          case '%':
            var e = ParseParameterEntity( WhiteSpace );
            try {
              PushEntity( _current.ResolvedUri, e );
            }
            catch( Exception ex ) {
              // BUG: need an error log.
              Console.WriteLine( ex.Message + _current.Context() );
            }
            ch = _current.Lastchar;
            break;
          default:
            _current.Error( "Unexpected character '{0}'", ch );
            break;
        }
      }
    }

    void ParseMarkup() {
      var ch = _current.ReadChar();
      if( ch != '!' ) {
        _current.Error( "Found '{0}', but expecing declaration starting with '<!'" );
        return;
      }
      ch = _current.ReadChar();
      switch( ch ) {
        case '-':
          ch = _current.ReadChar();
          if( ch != '-' ) _current.Error( "Expecting comment '<!--' but found {0}", ch );
          _current.ScanToEnd( _sb, "Comment", "-->" );
          break;
        case '[':
          ParseMarkedSection();
          break;
        default: {
            var token = _current.ScanToken( _sb, WhiteSpace, true );
            switch( token ) {
              case "ENTITY":
                ParseEntity();
                break;
              case "ELEMENT":
                ParseElementDecl();
                break;
              case "ATTLIST":
                ParseAttList();
                break;
              default:
                _current.Error( "Invalid declaration '<!{0}'.  Expecting 'ENTITY', 'ELEMENT' or 'ATTLIST'.", token );
                break;
            }
          }
          break;
      }
    }

    char ParseDeclComments() {
      var ch = _current.Lastchar;
      while( ch == '-' ) {
        ch = ParseDeclComment( true );
      }
      return ch;
    }

    char ParseDeclComment( bool full ) {
      // -^-...--
      // This method scans over a comment inside a markup declaration.
      var ch = _current.ReadChar();
      if( full && ch != '-' ) _current.Error( "Expecting comment delimiter '--' but found {0}", ch );
      _current.ScanToEnd( _sb, "Markup Comment", "--" );
      return _current.SkipWhitespace();
    }

    void ParseMarkedSection() {
      // <![^ name [ ... ]]>
      _current.ReadChar(); // move to next char.
      var name = ScanName( "[" );
      if( name.EqualsIgnoreCase( "INCLUDE" ) ) {
        ParseIncludeSection();
      }
      else if( name.EqualsIgnoreCase( "IGNORE" ) ) {
        ParseIgnoreSection();
      }
      else {
        _current.Error( "Unsupported marked section type '{0}'", name );
      }
    }

    [SuppressMessage( "Microsoft.Performance", "CA1822", Justification = "This is not yet implemented and will use 'this' in the future." )]
    [SuppressMessage( "Microsoft.Globalization", "CA1303", Justification = "The use of a literal here is only due to this not yet being implemented." )]
    private void ParseIncludeSection() {
      throw new NotImplementedException( "Include Section" );
    }

    void ParseIgnoreSection() {
      // <!-^-...-->
      var ch = _current.SkipWhitespace();
      if( ch != '[' ) _current.Error( "Expecting '[' but found {0}", ch );
      _current.ScanToEnd( _sb, "Conditional Section", "]]>" );
    }

    string ScanName( string term ) {
      // skip whitespace, scan name (which may be parameter entity reference
      // which is then expanded to a name)
      var ch = _current.SkipWhitespace();
      if( ch != '%' ) {
        return _current.ScanToken( _sb, term, true );
      }
      var e = ParseParameterEntity( term );
      // bugbug - need to support external and nested parameter entities
      if( !e.IsInternal ) throw new NotSupportedException( "External parameter entity resolution" );
      return e.Literal.Trim();
    }

    private Entity ParseParameterEntity( string term ) {
      // almost the same as current.ScanToken, except we also terminate on ';'
      _current.ReadChar();
      var name = _current.ScanToken( _sb, ";" + term, false );
      if( _current.Lastchar == ';' )
        _current.ReadChar();
      var e = GetParameterEntity( name );
      return e;
    }

    private Entity GetParameterEntity( string name ) {
      Entity e;
      _pentities.TryGetValue( name, out e );
      if( e == null )
        _current.Error( "Reference to undefined parameter entity '{0}'", name );

      return e;
    }

    /// <summary>
    /// Returns a dictionary for looking up entities by their <see cref="Entity.Literal"/> value.
    /// </summary>
    /// <returns>A dictionary for looking up entities by their <see cref="Entity.Literal"/> value.</returns>
    [SuppressMessage( "Microsoft.Design", "CA1024", Justification = "This method creates and copies a dictionary, so exposing it as a property is not appropriate." )]
    public Dictionary<string, Entity> GetEntitiesLiteralNameLookup() {
      return _entities.Values.ToDictionary( entity => entity.Literal, entity => entity );
    }

    private const string WhiteSpace = " \r\n\t";

    private void ParseEntity() {
      var ch = _current.SkipWhitespace();
      var pe = ( ch == '%' );
      if( pe ) {
        // parameter entity.
        _current.ReadChar(); // move to next char
        _current.SkipWhitespace();
      }
      var name = _current.ScanToken( _sb, WhiteSpace, true );
      ch = _current.SkipWhitespace();
      Entity e;
      if( ch == '"' || ch == '\'' ) {
        var literal = _current.ScanLiteral( _sb, ch );
        e = new Entity( name, literal );
      }
      else {
        string pubid = null;
        var tok = _current.ScanToken( _sb, WhiteSpace, true );
        if( Entity.IsLiteralType( tok ) ) {
          ch = _current.SkipWhitespace();
          var literal = _current.ScanLiteral( _sb, ch );
          e = new Entity( name, literal );
          e.SetLiteralType( tok );
        }
        else {
          var extid = tok;
          if( extid.EqualsIgnoreCase( "PUBLIC" ) ) {
            ch = _current.SkipWhitespace();
            if( ch == '"' || ch == '\'' ) {
              pubid = _current.ScanLiteral( _sb, ch );
            }
            else {
              _current.Error( "Expecting public identifier literal but found '{0}'", ch );
            }
          }
          else if( !extid.EqualsIgnoreCase( "SYSTEM" ) ) {
            _current.Error( "Invalid external identifier '{0}'.  Expecing 'PUBLIC' or 'SYSTEM'.", extid );
          }
          string uri = null;
          ch = _current.SkipWhitespace();
          if( ch == '"' || ch == '\'' ) {
            uri = _current.ScanLiteral( _sb, ch );
          }
          else if( ch != '>' ) {
            _current.Error( "Expecting system identifier literal but found '{0}'", ch );
          }
          e = new Entity( name, pubid, uri, _current.Proxy );
        }
      }
      ch = _current.SkipWhitespace();
      if( ch == '-' )
        ch = ParseDeclComments();
      if( ch != '>' ) {
        _current.Error( "Expecting end of entity declaration '>' but found '{0}'", ch );
      }
      if( pe )
        _pentities.Add( e.Name, e );
      else
        _entities.Add( e.Name, e );
    }

    private void ParseElementDecl() {
      var ch = _current.SkipWhitespace();
      var names = ParseNameGroup( ch, true );
      ch = char.ToUpperInvariant( _current.SkipWhitespace() );
      var sto = false;
      var eto = false;
      if( ch == 'O' || ch == '-' ) {
        sto = ( ch == 'O' ); // start tag optional?   
        _current.ReadChar();
        ch = char.ToUpperInvariant( _current.SkipWhitespace() );
        if( ch == 'O' || ch == '-' ) {
          eto = ( ch == 'O' ); // end tag optional? 
          _current.ReadChar();
        }
      }
      ch = _current.SkipWhitespace();
      var cm = ParseContentModel( ch );
      ch = _current.SkipWhitespace();

      string[] exclusions = null;
      string[] inclusions = null;

      if( ch == '-' ) {
        ch = _current.ReadChar();
        switch( ch ) {
          case '(':
            exclusions = ParseNameGroup( ch, true );
            ch = _current.SkipWhitespace();
            break;
          case '-':
            ch = ParseDeclComment( false );
            break;
          default:
            _current.Error( "Invalid syntax at '{0}'", ch );
            break;
        }
      }

      if( ch == '-' )
        ch = ParseDeclComments();

      if( ch == '+' ) {
        ch = _current.ReadChar();
        if( ch != '(' ) {
          _current.Error( "Expecting inclusions name group", ch );
        }
        inclusions = ParseNameGroup( ch, true );
        ch = _current.SkipWhitespace();
      }

      if( ch == '-' )
        ch = ParseDeclComments();


      if( ch != '>' ) {
        _current.Error( "Expecting end of ELEMENT declaration '>' but found '{0}'", ch );
      }

      foreach( var atom in names.Select( name => name.ToUpperInvariant() ) ) {
        _elements.Add( atom, new ElementDecl( atom, sto, eto, cm, inclusions, exclusions ) );
      }
    }

    const string Ngterm = " \r\n\t|,)";
    string[] ParseNameGroup( char ch, bool nmtokens ) {
      var names = new ArrayList();
      if( ch == '(' ) {
        _current.ReadChar();
        ch = _current.SkipWhitespace();
        while( ch != ')' ) {
          // skip whitespace, scan name (which may be parameter entity reference
          // which is then expanded to a name)                    
          ch = _current.SkipWhitespace();
          if( ch == '%' ) {
            var e = ParseParameterEntity( Ngterm );
            PushEntity( _current.ResolvedUri, e );
            ParseNameList( names );
            PopEntity();
          }
          else {
            var token = _current.ScanToken( _sb, Ngterm, nmtokens );
            token = token.ToUpperInvariant();
            names.Add( token );
          }
          ch = _current.SkipWhitespace();
          if( ch == '|' || ch == ',' ) ch = _current.ReadChar();
        }
        _current.ReadChar(); // consume ')'
      }
      else {
        var name = _current.ScanToken( _sb, WhiteSpace, nmtokens );
        name = name.ToUpperInvariant();
        names.Add( name );
      }
      return (string[]) names.ToArray( typeof( string ) );
    }

    void ParseNameList( IList names ) {
      var ch = _current.SkipWhitespace();
      while( ch != Entity.EOF ) {
        string name;
        if( ch == '%' ) {
          var e = ParseParameterEntity( Ngterm );
          PushEntity( _current.ResolvedUri, e );
          ParseNameList( names );
          PopEntity();
        }
        else {
          name = _current.ScanToken( _sb, Ngterm, true );
          name = name.ToUpperInvariant();
          names.Add( name );
        }
        ch = _current.SkipWhitespace();
        if( ch != '|' ) continue;
        _current.ReadChar();
        ch = _current.SkipWhitespace();
      }
    }

    const string Dcterm = " \r\n\t>";
    private ContentModel ParseContentModel( char ch ) {
      var cm = new ContentModel();
      switch( ch ) {
        case '(':
          _current.ReadChar();
          ParseModel( ')', cm );
          ch = _current.ReadChar();
          if( ch == '?' || ch == '+' || ch == '*' ) {
            cm.AddOccurrence( ch );
            _current.ReadChar();
          }
          break;
        case '%': {
            var e = ParseParameterEntity( Dcterm );
            PushEntity( _current.ResolvedUri, e );
            cm = ParseContentModel( _current.Lastchar );
            PopEntity(); // bugbug should be at EOF.
          }
          break;
        default: {
            var dc = ScanName( Dcterm );
            cm.SetDeclaredContent( dc );
          }
          break;
      }
      return cm;
    }

    const string Cmterm = " \r\n\t,&|()?+*";
    void ParseModel( char cmt, ContentModel cm ) {
      // Called when part of the model is made up of the contents of a parameter entity
      var depth = cm.CurrentDepth;
      var ch = _current.SkipWhitespace();
      while( ch != cmt || cm.CurrentDepth > depth ) // the entity must terminate while inside the content model.
      {
        if( ch == Entity.EOF ) {
          _current.Error( "Content Model was not closed" );
        }
        switch( ch ) {
          case '%': {
              var e = ParseParameterEntity( Cmterm );
              PushEntity( _current.ResolvedUri, e );
              ParseModel( Entity.EOF, cm );
              PopEntity();
              ch = _current.SkipWhitespace();
            }
            break;
          case '(':
            cm.PushGroup();
            _current.ReadChar();// consume '('
            ch = _current.SkipWhitespace();
            break;
          case ')':
            ch = _current.ReadChar();// consume ')'
            if( ch == '*' || ch == '+' || ch == '?' ) {
              cm.AddOccurrence( ch );
              _current.ReadChar();
            }
            if( cm.PopGroup() < depth ) {
              _current.Error( "Parameter entity cannot close a paren outside it's own scope" );
            }
            ch = _current.SkipWhitespace();
            break;
          case '&':
          case '|':
          case ',':
            cm.AddConnector( ch );
            _current.ReadChar(); // skip connector
            ch = _current.SkipWhitespace();
            break;
          default: {
              string token;
              if( ch == '#' ) {
                _current.ReadChar();
                token = "#" + _current.ScanToken( _sb, Cmterm, true ); // since '#' is not a valid name character.
              }
              else {
                token = _current.ScanToken( _sb, Cmterm, true );
              }

              token = token.ToUpperInvariant();
              ch = _current.Lastchar;
              if( ch == '?' || ch == '+' || ch == '*' ) {
                cm.PushGroup();
                cm.AddSymbol( token );
                cm.AddOccurrence( ch );
                cm.PopGroup();
                _current.ReadChar(); // skip connector
                ch = _current.SkipWhitespace();
              }
              else {
                cm.AddSymbol( token );
                ch = _current.SkipWhitespace();
              }
            }
            break;
        }
      }
    }

    void ParseAttList() {
      var ch = _current.SkipWhitespace();
      var names = ParseNameGroup( ch, true );
      var attlist = new Dictionary<string, AttDef>();
      ParseAttList( attlist, '>' );
      foreach( var name in names ) {
        ElementDecl e;
        if( !_elements.TryGetValue( name, out e ) ) {
          _current.Error( "ATTLIST references undefined ELEMENT {0}", name );
        }

        e.AddAttDefs( attlist );
      }
    }

    const string Peterm = " \t\r\n>";
    void ParseAttList( IDictionary<string, AttDef> list, char term ) {
      var ch = _current.SkipWhitespace();
      while( ch != term ) {
        switch( ch ) {
          case '%': {
              var e = ParseParameterEntity( Peterm );
              PushEntity( _current.ResolvedUri, e );
              ParseAttList( list, Entity.EOF );
              PopEntity();
              _current.SkipWhitespace();
            }
            break;
          case '-':
            ParseDeclComments();
            break;
          default: {
              var a = ParseAttDef();
              list.Add( a.Name, a );
            }
            break;
        }
        ch = _current.SkipWhitespace();
      }
    }

    AttDef ParseAttDef() {
      var name = ScanName( WhiteSpace );
      name = name.ToUpperInvariant();
      var attdef = new AttDef( name );

      var ch = _current.SkipWhitespace();
      if( ch == '-' )
        ch = ParseDeclComments();

      ParseAttType( ch, attdef );

      ch = _current.SkipWhitespace();
      if( ch == '-' )
        ch = ParseDeclComments();

      ParseAttDefault( ch, attdef );

      ch = _current.SkipWhitespace();
      if( ch == '-' )
        ParseDeclComments();

      return attdef;
    }

    void ParseAttType( char ch, AttDef attdef ) {
      if( ch == '%' ) {
        var e = ParseParameterEntity( WhiteSpace );
        PushEntity( _current.ResolvedUri, e );
        ParseAttType( _current.Lastchar, attdef );
        PopEntity(); // bugbug - are we at the end of the entity?
        return;
      }

      if( ch == '(' ) {
        attdef.SetEnumeratedType( ParseNameGroup( ch, false ), AttributeType.ENUMERATION );
      }
      else {
        var token = ScanName( WhiteSpace );
        if( token.EqualsIgnoreCase( "NOTATION" ) ) {
          ch = _current.SkipWhitespace();
          if( ch != '(' ) {
            _current.Error( "Expecting name group '(', but found '{0}'", ch );
          }
          attdef.SetEnumeratedType( ParseNameGroup( ch, true ), AttributeType.NOTATION );
        }
        else {
          attdef.SetType( token );
        }
      }
    }

    void ParseAttDefault( char ch, AttDef attdef ) {
      if( ch == '%' ) {
        var e = ParseParameterEntity( WhiteSpace );
        PushEntity( _current.ResolvedUri, e );
        ParseAttDefault( _current.Lastchar, attdef );
        PopEntity(); // bugbug - are we at the end of the entity?        
        return;
      }

      var hasdef = true;
      if( ch == '#' ) {
        _current.ReadChar();
        var token = _current.ScanToken( _sb, WhiteSpace, true );
        hasdef = attdef.SetPresence( token );
        ch = _current.SkipWhitespace();
      }
      if( !hasdef ) return;

      if( ch == '\'' || ch == '"' ) {
        var lit = _current.ScanLiteral( _sb, ch );
        attdef.Default = lit;
        _current.SkipWhitespace();
      }
      else {
        var name = _current.ScanToken( _sb, WhiteSpace, false );
        name = name.ToUpperInvariant();
        attdef.Default = name; // bugbug - must be one of the enumerated names.
        _current.SkipWhitespace();
      }
    }
  }
}