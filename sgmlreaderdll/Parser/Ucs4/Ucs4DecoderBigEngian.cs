using System;

namespace SgmlReaderDll.Parser.Ucs4 {
  internal class Ucs4DecoderBigEngian : Ucs4Decoder {
    internal override uint GetCode( int i, byte[] bytes ) {
      return (UInt32)(((bytes[i + 3]) << 24) | (bytes[i + 2] << 16) | (bytes[i + 1] << 8) | (bytes[i]));
    }
  }
}