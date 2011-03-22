﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using SgmlReaderDll.Parser.Ucs4;

namespace SgmlReaderDll.Parser {
  internal class HtmlStream : TextReader
  {
    private Stream stm;
    private byte[] rawBuffer;
    private int rawPos;
    private int rawUsed;
    private Encoding m_encoding;
    private Decoder m_decoder;
    private char[] m_buffer;
    private int used;
    private int pos;
    private const int BUFSIZE = 16384;
    private const int EOF = -1;

    public HtmlStream(Stream stm, Encoding defaultEncoding)
    {            
      if (defaultEncoding == null) defaultEncoding = Encoding.UTF8; // default is UTF8
      if (!stm.CanSeek){
        // Need to be able to seek to sniff correctly.
        stm = CopyToMemoryStream(stm);
      }
      this.stm = stm;
      rawBuffer = new Byte[BUFSIZE];
      rawUsed = stm.Read(rawBuffer, 0, 4); // maximum byte order mark
      this.m_buffer = new char[BUFSIZE];

      // Check byte order marks
      this.m_decoder = AutoDetectEncoding(rawBuffer, ref rawPos, rawUsed);
      int bom = rawPos;
      if (this.m_decoder == null)
      {
        this.m_decoder = defaultEncoding.GetDecoder();
        rawUsed += stm.Read(rawBuffer, 4, BUFSIZE-4);                
        DecodeBlock();
        // Now sniff to see if there is an XML declaration or HTML <META> tag.
        Decoder sd = SniffEncoding();
        if (sd != null) {
          this.m_decoder = sd;
        }
      }            

      // Reset to get ready for Read()
      this.stm.Seek(0, SeekOrigin.Begin);
      this.pos = this.used = 0;
      // skip bom
      if (bom>0){
        stm.Read(this.rawBuffer, 0, bom);
      }
      this.rawPos = this.rawUsed = 0;
            
    }

    public Encoding Encoding
    {
      get
      {
        return this.m_encoding;
      }
    }

    private static Stream CopyToMemoryStream(Stream s)
    {
      int size = 100000; // large heap is more efficient
      byte[] copyBuff = new byte[size];
      int len;
      MemoryStream r = new MemoryStream();
      while ((len = s.Read(copyBuff, 0, size)) > 0)
        r.Write(copyBuff, 0, len);

      r.Seek(0, SeekOrigin.Begin);                            
      s.Close();
      return r;
    }

    internal void DecodeBlock() {
      // shift current chars to beginning.
      if (pos > 0) {
        if (pos < used) {
          System.Array.Copy(m_buffer, pos, m_buffer, 0, used - pos);
        }
        used -= pos;
        pos = 0;
      }
      int len = m_decoder.GetCharCount(rawBuffer, rawPos, rawUsed - rawPos);
      int available = m_buffer.Length - used;
      if (available < len) {
        char[] newbuf = new char[m_buffer.Length + len];
        System.Array.Copy(m_buffer, pos, newbuf, 0, used - pos);
        m_buffer = newbuf;
      }
      used = pos + m_decoder.GetChars(rawBuffer, rawPos, rawUsed - rawPos, m_buffer, pos);
      rawPos = rawUsed; // consumed the whole buffer!
    }
    internal static Decoder AutoDetectEncoding(byte[] buffer, ref int index, int length) {
      if (4 <= (length - index)) {
        uint w = (uint)buffer[index + 0] << 24 | (uint)buffer[index + 1] << 16 | (uint)buffer[index + 2] << 8 | (uint)buffer[index + 3];
        // see if it's a 4-byte encoding
        switch (w) {
          case 0xfefffeff: 
            index += 4; 
            return new Ucs4DecoderBigEngian();

          case 0xfffefffe: 
            index += 4; 
            return new Ucs4DecoderLittleEndian();

          case 0x3c000000: 
            goto case 0xfefffeff;

          case 0x0000003c: 
            goto case 0xfffefffe;
        }
        w >>= 8;
        if (w == 0xefbbbf) {
          index += 3;
          return Encoding.UTF8.GetDecoder();
        }
        w >>= 8;
        switch (w) {
          case 0xfeff: 
            index += 2; 
            return UnicodeEncoding.BigEndianUnicode.GetDecoder();

          case 0xfffe: 
            index += 2; 
            return new UnicodeEncoding(false, false).GetDecoder();

          case 0x3c00: 
            goto case 0xfeff;

          case 0x003c: 
            goto case 0xfffe;
        }
      }
      return null;
    }
    private int ReadChar() {
      // Read only up to end of current buffer then stop.
      if (pos < used) return m_buffer[pos++];
      return EOF;
    }
    private int PeekChar() {
      int ch = ReadChar();
      if (ch != EOF) {
        pos--;
      }
      return ch;
    }
    private bool SniffPattern(string pattern) {
      int ch = PeekChar();
      if (ch != pattern[0]) return false;
      for (int i = 0, n = pattern.Length; ch != EOF && i < n; i++) {
        ch = ReadChar();
        char m = pattern[i];
        if (ch != m) {
          return false;
        }
      }
      return true;
    }
    private void SniffWhitespace() {
      char ch = (char)PeekChar();
      while (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') {
        int i = pos;
        ch = (char)ReadChar();
        if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n')
          pos = i;
      }
    }

    private string SniffLiteral() {
      int quoteChar = PeekChar();
      if (quoteChar == '\'' || quoteChar == '"') {
        ReadChar();// consume quote char
        int i = this.pos;
        int ch = ReadChar();
        while (ch != EOF && ch != quoteChar) {
          ch = ReadChar();
        }
        return (pos>i) ? new string(m_buffer, i, pos - i - 1) : "";
      }
      return null;
    }
    private string SniffAttribute(string name) {
      SniffWhitespace();
      string id = SniffName();
      if (string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) {
        SniffWhitespace();
        if (SniffPattern("=")) {
          SniffWhitespace();
          return SniffLiteral();
        }
      }
      return null;
    }
    private string SniffAttribute(out string name) {
      SniffWhitespace();
      name = SniffName();
      if (name != null){
        SniffWhitespace();
        if (SniffPattern("=")) {
          SniffWhitespace();
          return SniffLiteral();
        }
      }
      return null;
    }
    private void SniffTerminator(string term) {
      int ch = ReadChar();
      int i = 0;
      int n = term.Length;
      while (i < n && ch != EOF) {
        if (term[i] == ch) {
          i++;
          if (i == n) break;
        } else {
          i = 0; // reset.
        }
        ch = ReadChar();
      }
    }

    internal Decoder SniffEncoding()
    {
      Decoder decoder = null;
      if (SniffPattern("<?xml"))
      {
        string version = SniffAttribute("version");
        if (version != null)
        {
          string encoding = SniffAttribute("encoding");
          if (encoding != null)
          {
            try
            {
              Encoding enc = Encoding.GetEncoding(encoding);
              if (enc != null)
              {
                this.m_encoding = enc;
                return enc.GetDecoder();
              }
            }
            catch (ArgumentException)
            {
              // oh well then.
            }
          }
          SniffTerminator(">");
        }
      } 
      if (decoder == null) {
        return SniffMeta();
      }
      return null;
    }

    internal Decoder SniffMeta()
    {
      int i = ReadChar();            
      while (i != EOF)
      {
        char ch = (char)i;
        if (ch == '<')
        {
          string name = SniffName();
          if (name != null && StringUtilities.EqualsIgnoreCase(name, "meta"))
          {
            string httpequiv = null;
            string content = null;
            while (true)
            {
              string value = SniffAttribute(out name);
              if (name == null)
                break;

              if (StringUtilities.EqualsIgnoreCase(name, "http-equiv"))
              {
                httpequiv = value;
              }
              else if (StringUtilities.EqualsIgnoreCase(name, "content"))
              {
                content = value;
              }
            }

            if (httpequiv != null && StringUtilities.EqualsIgnoreCase(httpequiv, "content-type") && content != null)
            {
              int j = content.IndexOf("charset");
              if (j >= 0)
              {
                //charset=utf-8
                j = content.IndexOf("=", j);
                if (j >= 0)
                {
                  j++;
                  int k = content.IndexOf(";", j);
                  if (k<0) k = content.Length;
                  string charset = content.Substring(j, k-j).Trim();
                  try
                  {
                    Encoding e = Encoding.GetEncoding(charset);
                    this.m_encoding = e;
                    return e.GetDecoder();
                  } catch (ArgumentException) {}
                }                                
              }
            }
          }
        }
        i = ReadChar();

      }
      return null;
    }

    internal string SniffName()
    {
      int c = PeekChar();
      if (c == EOF)
        return null;
      char ch = (char)c;
      int start = pos;
      while (pos < used - 1 && (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == ':'))
        ch = m_buffer[++pos];

      if (start == pos)
        return null;

      return new string(m_buffer, start, pos - start);
    }

    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    internal void SkipWhitespace()
    {
      char ch = (char)PeekChar();
      while (pos < used - 1 && (ch == ' ' || ch == '\r' || ch == '\n'))
        ch = m_buffer[++pos];
    }

    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    internal void SkipTo(char what)
    {
      char ch = (char)PeekChar();
      while (pos < used - 1 && (ch != what))
        ch = m_buffer[++pos];
    }

    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    internal string ParseAttribute()
    {
      SkipTo('=');
      if (pos < used)
      {
        pos++;
        SkipWhitespace();
        if (pos < used) {
          char quote = m_buffer[pos];
          pos++;
          int start = pos;
          SkipTo(quote);
          if (pos < used) {
            string result = new string(m_buffer, start, pos - start);
            pos++;
            return result;
          }
        }
      }
      return null;
    }
    public override int Peek() {
      int result = Read();
      if (result != EOF) {
        pos--;
      }
      return result;
    }
    public override int Read()
    {
      if (pos == used)
      {
        rawUsed = stm.Read(rawBuffer, 0, rawBuffer.Length);
        rawPos = 0;
        if (rawUsed == 0) return EOF;
        DecodeBlock();
      }
      if (pos < used) return m_buffer[pos++];
      return -1;
    }

    public override int Read(char[] buffer, int start, int length) {
      if (pos == used) {
        rawUsed = stm.Read(rawBuffer, 0, rawBuffer.Length);
        rawPos = 0;
        if (rawUsed == 0) return -1;
        DecodeBlock();
      }
      if (pos < used) {
        length = Math.Min(used - pos, length);
        Array.Copy(this.m_buffer, pos, buffer, start, length);
        pos += length;
        return length;
      }
      return 0;
    }

    public override int ReadBlock(char[] data, int index, int count)
    {
      return Read(data, index, count);
    }

    // Read up to end of line, or full buffer, whichever comes first.
    [SuppressMessage("Microsoft.Performance", "CA1811", Justification = "Kept for potential future usage.")]
    public int ReadLine(char[] buffer, int start, int length)
    {
      int i = 0;
      int ch = ReadChar();
      while (ch != EOF) {
        buffer[i+start] = (char)ch;
        i++;
        if (i+start == length) 
          break; // buffer is full

        if (ch == '\r' ) {
          if (PeekChar() == '\n') {
            ch = ReadChar();
            buffer[i + start] = (char)ch;
            i++;
          }
          break;
        } else if (ch == '\n') {
          break;
        }
        ch = ReadChar();
      }
      return i;
    }

    public override string ReadToEnd() {
      char[] buffer = new char[100000]; // large block heap is more efficient
      int len = 0;
      StringBuilder sb = new StringBuilder();
      while ((len = Read(buffer, 0, buffer.Length)) > 0) {
        sb.Append(buffer, 0, len);
      }
      return sb.ToString();
    }
    public override void Close() {
      stm.Close();
    }
  }
}