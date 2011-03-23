using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using SgmlReaderDll.Extensions;
using SgmlReaderDll.Parser.Ucs4;

namespace SgmlReaderDll.Parser {
  internal class HtmlStream : TextReader {
    private readonly Stream _stream;
    private readonly byte[] _rawBuffer;
    private int _rawPos;
    private int _rawUsed;
    private Decoder _decoder;
    private char[] _buffer;
    private int _used;
    private int _pos;
    private const int Bufsize = 16384;
    private const int Eof = -1;

    public HtmlStream( Stream stream, Encoding defaultEncoding ) {
      if( defaultEncoding == null ) defaultEncoding = Encoding.UTF8; // default is UTF8
      
      if( !stream.CanSeek ) {
        // Need to be able to seek to sniff correctly.
        stream = CopyToMemoryStream( stream );
      }
      
      _stream = stream;
      _rawBuffer = new Byte[ Bufsize ];
      _rawUsed = stream.Read( _rawBuffer, 0, 4 ); // maximum byte order mark
      _buffer = new char[ Bufsize ];

      // Check byte order marks
      _decoder = AutoDetectEncoding( _rawBuffer, ref _rawPos, _rawUsed );
      var bom = _rawPos;
      if( _decoder == null ) {
        InitializeDecoder( stream, defaultEncoding );
      }

      // Reset to get ready for Read()
      _stream.Seek( 0, SeekOrigin.Begin );
      _pos = _used = 0;

      // skip bom
      if( bom > 0 ) {
        stream.Read( _rawBuffer, 0, bom );
      }
      _rawPos = _rawUsed = 0;
    }

    private void InitializeDecoder( Stream stream, Encoding defaultEncoding ) {
      _decoder = defaultEncoding.GetDecoder();
      _rawUsed += stream.Read( _rawBuffer, 4, Bufsize - 4 );
      DecodeBlock();
      // Now sniff to see if there is an XML declaration or HTML <META> tag.
      var sd = SniffEncoding();
      if( sd != null ) {
        _decoder = sd;
      }
    }

    public Encoding Encoding { get; private set; }

    private static Stream CopyToMemoryStream( Stream s ) {
      const int size = 100000;
      var copyBuff = new byte[ size ];
      int len;
      var r = new MemoryStream();

      while( ( len = s.Read( copyBuff, 0, size ) ) > 0 )
        r.Write( copyBuff, 0, len );

      r.Seek( 0, SeekOrigin.Begin );
      s.Close();

      return r;
    }

    internal void DecodeBlock() {
      // shift current chars to beginning.
      if( _pos > 0 ) {
        if( _pos < _used ) {
          Array.Copy( _buffer, _pos, _buffer, 0, _used - _pos );
        }
        _used -= _pos;
        _pos = 0;
      }

      var len = _decoder.GetCharCount( _rawBuffer, _rawPos, _rawUsed - _rawPos );
      var available = _buffer.Length - _used;

      if( available < len ) {
        var newbuf = new char[ _buffer.Length + len ];
        Array.Copy( _buffer, _pos, newbuf, 0, _used - _pos );
        _buffer = newbuf;
      }

      _used = _pos + _decoder.GetChars( _rawBuffer, _rawPos, _rawUsed - _rawPos, _buffer, _pos );
      _rawPos = _rawUsed; // consumed the whole buffer!
    }

    internal static Decoder AutoDetectEncoding( byte[] buffer, ref int index, int length ) {
      if( 4 <= ( length - index ) ) {
        var w = (uint) buffer[ index + 0 ] << 24 | (uint) buffer[ index + 1 ] << 16 | (uint) buffer[ index + 2 ] << 8 | buffer[ index + 3 ];
        // see if it's a 4-byte encoding
        switch( w ) {
          case 0x3c000000:
          case 0xfefffeff:
            index += 4;
            return new Ucs4DecoderBigEngian();
          case 0x0000003c:
          case 0xfffefffe:
            index += 4;
            return new Ucs4DecoderLittleEndian();
        }
        w >>= 8;
        if( w == 0xefbbbf ) {
          index += 3;
          return Encoding.UTF8.GetDecoder();
        }
        w >>= 8;
        switch( w ) {
          case 0x3c00:
          case 0xfeff:
            index += 2;
            return Encoding.BigEndianUnicode.GetDecoder();
          case 0x003c:
          case 0xfffe:
            index += 2;
            return new UnicodeEncoding( false, false ).GetDecoder();
        }
      }
      return null;
    }

    private int ReadChar() {
      // Read only up to end of current buffer then stop.
      return _pos < _used ? _buffer[ _pos++ ] : Eof;
    }

    private int PeekChar() {
      var ch = ReadChar();
      if( ch != Eof ) {
        _pos--;
      }
      return ch;
    }

    private bool SniffPattern( string pattern ) {
      var ch = PeekChar();
      if( ch != pattern[ 0 ] ) return false;
      var n = pattern.Length;

      for( var i = 0; ch != Eof && i < n; i++ ) {
        ch = ReadChar();
        var m = pattern[ i ];
        if( ch != m ) {
          return false;
        }
      }

      return true;
    }

    private void SniffWhitespace() {
      var ch = (char) PeekChar();

      while( char.IsWhiteSpace( ch ) ) {
        var i = _pos;
        ch = (char) ReadChar();
        if( !char.IsWhiteSpace( ch ) )
          _pos = i;
      }
    }

    private string SniffLiteral() {
      var quoteChar = PeekChar();

      if( quoteChar == '\'' || quoteChar == '"' ) {
        ReadChar();// consume quote char
        var i = _pos;
        var ch = ReadChar();
        while( ch != Eof && ch != quoteChar ) {
          ch = ReadChar();
        }
        return ( _pos > i ) ? new string( _buffer, i, _pos - i - 1 ) : "";
      }

      return null;
    }

    private string SniffAttribute( string name ) {
      SniffWhitespace();
      var id = SniffName();

      if( name.Equals( id, StringComparison.OrdinalIgnoreCase ) ) {
        SniffWhitespace();
        if( SniffPattern( "=" ) ) {
          SniffWhitespace();
          return SniffLiteral();
        }
      }

      return null;
    }

    private string SniffAttribute( out string name ) {
      SniffWhitespace();
      name = SniffName();

      if( name != null ) {
        SniffWhitespace();
        if( SniffPattern( "=" ) ) {
          SniffWhitespace();
          return SniffLiteral();
        }
      }

      return null;
    }

    private void SniffTerminator( string term ) {
      var ch = ReadChar();
      var i = 0;
      var n = term.Length;

      while( i < n && ch != Eof ) {
        if( term[ i ] == ch ) {
          i++;
          if( i == n ) break;
        }
        else {
          i = 0; // reset.
        }
        ch = ReadChar();
      }
    }

    internal Decoder SniffEncoding() {
      if( SniffPattern( "<?xml" ) ) {
        var version = SniffAttribute( "version" );
        if( version != null ) {
          var encoding = SniffAttribute( "encoding" );
          if( encoding != null ) {
            try {
              var enc = Encoding.GetEncoding( encoding );
              Encoding = enc;
              return enc.GetDecoder();
            }
            catch( ArgumentException ) {
              // oh well then.
            }
          }
          SniffTerminator( ">" );
        }
      }

      return SniffMeta();
    }

    internal Decoder SniffMeta() {
      var i = ReadChar();

      while( i != Eof ) {
        var ch = (char) i;

        if( ch == '<' ) {
          var name = SniffName();

          if( name != null && name.EqualsIgnoreCase( "meta" ) ) {
            string httpequiv = null;
            string content = null;

            while( true ) {
              var value = SniffAttribute( out name );
              if( name == null )
                break;

              if( name.EqualsIgnoreCase( "http-equiv" ) ) {
                httpequiv = value;
              }
              else if( name.EqualsIgnoreCase( "content" ) ) {
                content = value;
              }
            }

            if( httpequiv != null && httpequiv.EqualsIgnoreCase( "content-type" ) && content != null ) {
              var j = content.IndexOf( "charset" );
              if( j >= 0 ) {
                //charset=utf-8
                j = content.IndexOf( "=", j );
                if( j >= 0 ) {
                  j++;
                  var k = content.IndexOf( ";", j );
                  if( k < 0 ) k = content.Length;
                  var charset = content.Substring( j, k - j ).Trim();
                  try {
                    var e = Encoding.GetEncoding( charset );
                    Encoding = e;
                    return e.GetDecoder();
                  }
                  catch( ArgumentException ) { }
                }
              }
            }
          }
        }
        i = ReadChar();
      }

      return null;
    }

    internal string SniffName() {
      var c = PeekChar();
      if( c == Eof )
        return null;

      var ch = (char) c;
      var start = _pos;

      while( _pos < _used - 1 && ( char.IsLetterOrDigit( ch ) || ch == '-' || ch == '_' || ch == ':' ) )
        ch = _buffer[ ++_pos ];

      return start == _pos ? null : new string( _buffer, start, _pos - start );
    }

    internal void SkipWhitespace() {
      var ch = (char) PeekChar();

      while( _pos < _used - 1 && char.IsWhiteSpace( ch ) )
        ch = _buffer[ ++_pos ];
    }

    internal void SkipTo( char what ) {
      var ch = (char) PeekChar();

      while( _pos < _used - 1 && ( ch != what ) )
        ch = _buffer[ ++_pos ];
    }

    public override int Peek() {
      var result = Read();

      if( result != Eof ) {
        _pos--;
      }

      return result;
    }

    public override int Read() {
      if( _pos == _used ) {
        _rawUsed = _stream.Read( _rawBuffer, 0, _rawBuffer.Length );
        _rawPos = 0;
        if( _rawUsed == 0 ) return Eof;
        DecodeBlock();
      }

      if( _pos < _used ) return _buffer[ _pos++ ];

      return -1;
    }

    public override int Read( char[] buffer, int start, int length ) {
      if( _pos == _used ) {
        _rawUsed = _stream.Read( _rawBuffer, 0, _rawBuffer.Length );
        _rawPos = 0;
        if( _rawUsed == 0 ) return -1;
        DecodeBlock();
      }

      if( _pos < _used ) {
        length = Math.Min( _used - _pos, length );
        Array.Copy( _buffer, _pos, buffer, start, length );
        _pos += length;
        return length;
      }

      return 0;
    }

    public override int ReadBlock( char[] data, int index, int count ) {
      return Read( data, index, count );
    }

    // Read up to end of line, or full buffer, whichever comes first.
    [SuppressMessage( "Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage." )]
    public int ReadLine( char[] buffer, int start, int length ) {
      var i = 0;
      var ch = ReadChar();

      while( ch != Eof ) {
        buffer[ i + start ] = (char) ch;
        i++;
        if( i + start == length )
          break; // buffer is full

        switch( ch ) {
          case '\r':
            if( PeekChar() == '\n' ) {
              ch = ReadChar();
              buffer[ i + start ] = (char) ch;
              i++;
            }
            break;
          case '\n':
            break;
        }
        ch = ReadChar();
      }

      return i;
    }

    public override string ReadToEnd() {
      var buffer = new char[ 100000 ]; // large block heap is more efficient
      int len;
      var sb = new StringBuilder();

      while( ( len = Read( buffer, 0, buffer.Length ) ) > 0 ) {
        sb.Append( buffer, 0, len );
      }

      return sb.ToString();
    }

    public override void Close() {
      _stream.Close();
    }
  }
}