using System;
using System.Globalization;
using System.Text;

namespace SgmlReaderDll.Parser.Ucs4 {
  internal abstract class Ucs4Decoder : Decoder {
    internal byte[] Temp = new byte[ 4 ];
    internal int TempBytes;

    public override int GetCharCount( byte[] bytes, int index, int count ) {
      return ( count + TempBytes ) / 4;
    }

    internal int GetFullChars( byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex ) {
      int i, j;
      byteCount += byteIndex;
      for( i = byteIndex, j = charIndex; i + 3 < byteCount; ) {
        var code = GetCode( i, bytes );
        if( code > 0x10FFFF ) {
          throw new SgmlParseException( string.Format( CultureInfo.CurrentUICulture, "Invalid character 0x{0:x} in encoding", code ) );
        }
        if( code > 0xFFFF ) {
          chars[ j ] = UnicodeToUtf16( code );
          j++;
        }
        else {
          if( code >= 0xD800 && code <= 0xDFFF ) {
            throw new SgmlParseException( string.Format( CultureInfo.CurrentUICulture, "Invalid character 0x{0:x} in encoding", code ) );
          }
          chars[ j ] = (char) code;
        }
        j++;
        i += 4;
      }
      return j - charIndex;
    }

    internal abstract uint GetCode( int i, byte[] bytes );

    public override int GetChars( byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex ) {
      var i = TempBytes;

      if( TempBytes > 0 ) {
        for( ; i < 4; i++ ) {
          Temp[ i ] = bytes[ byteIndex ];
          byteIndex++;
          byteCount--;
        }
        i = 1;
        GetFullChars( Temp, 0, 4, chars, charIndex );
        charIndex++;
      }
      else
        i = 0;
      i = GetFullChars( bytes, byteIndex, byteCount, chars, charIndex ) + i;

      var j = ( TempBytes + byteCount ) % 4;
      byteCount += byteIndex;
      byteIndex = byteCount - j;
      TempBytes = 0;

      if( byteIndex >= 0 )
        for( ; byteIndex < byteCount; byteIndex++ ) {
          Temp[ TempBytes ] = bytes[ byteIndex ];
          TempBytes++;
        }
      return i;
    }

    internal static char UnicodeToUtf16( UInt32 code ) {
      var lowerByte = (byte) ( 0xD7C0 + ( code >> 10 ) );
      var higherByte = (byte) ( 0xDC00 | code & 0x3ff );
      return ( (char) ( ( higherByte << 8 ) | lowerByte ) );
    }
  }
}