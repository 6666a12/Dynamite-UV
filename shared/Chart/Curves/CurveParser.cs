using System.Globalization;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Bounded arithmetic-only recursive descent. Exponentiation is right-associative and binds above unary minus.</summary>
internal sealed class CurveParser
{
    private readonly string _source;
    private int _offset, _nodes, _depth;
    private CurveParser(string source) => _source = source;
    internal static CurveTerm Parse(string source)
    {
        var p = new CurveParser(source); var term = p.Sum();
        p.Skip(); if (p._offset != source.Length) throw p.Error("Unexpected token");
        return term;
    }
    private FormatException Error(string message) => new($"{message} at column {_offset + 1}.");
    private void Skip() { while (_offset < _source.Length && char.IsWhiteSpace(_source[_offset])) _offset++; }
    private bool Eat(char c)
    {
        Skip(); if (_offset >= _source.Length || _source[_offset] != c) return false;
        _offset++; return true;
    }
    private void Need(char c) { if (!Eat(c)) throw Error($"Expected '{c}'"); }
    private CurveTerm N(string op, CurveTerm? left=null, CurveTerm? right=null, double number=0)
    {
        if (++_nodes > 128) throw Error("Formula exceeds 128 syntax nodes");
        return new(op, left, right, number);
    }
    private CurveTerm Sum()
    {
        var n = Product();
        while (true) { if (Eat('+')) n = N("+",n,Product()); else if (Eat('-')) n=N("-",n,Product()); else return n; }
    }
    private CurveTerm Product()
    {
        var n = Unary();
        while (true) { if (Eat('*')) n=N("*",n,Unary()); else if (Eat('/')) n=N("/",n,Unary()); else return n; }
    }
    private CurveTerm Unary()
    {
        if (++_depth > 32) throw Error("Formula nesting exceeds 32 levels");
        CurveTerm n;
        if (Eat('+')) n = Unary();
        else if (Eat('-')) n=N("neg",Unary());
        else { n=Primary(); if (Eat('^')) n=N("^",n,Unary()); }
        _depth--; return n;
    }
    private CurveTerm Primary()
    {
        if (Eat('(')) { var inside=Sum(); Need(')'); return inside; }
        Skip(); if (_offset >= _source.Length) throw Error("Expected a number, variable or function");
        if (char.IsAsciiDigit(_source[_offset]) || _source[_offset]=='.') return Number();
        if (!char.IsAsciiLetter(_source[_offset])) throw Error("Unexpected character");
        var start=_offset++; while (_offset<_source.Length && char.IsAsciiLetterOrDigit(_source[_offset])) _offset++;
        var name=_source[start.._offset];
        if (name is "t" or "e" or "pi") return N(name);
        if (name is not ("abs" or "sin" or "cos" or "sqrt" or "exp" or "log" or "min" or "max"))
            throw new FormatException($"Unsupported name '{name}' at column {start + 1}.");
        Need('('); var a=Sum(); CurveTerm? b=null;
        if (name is "min" or "max") { Need(','); b=Sum(); }
        Need(')'); return N(name,a,b);
    }
    private CurveTerm Number()
    {
        var start=_offset; bool digits=false, nonzero=false;
        void Digits()
        {
            while (_offset<_source.Length && char.IsAsciiDigit(_source[_offset]))
            { digits=true; nonzero |= _source[_offset]!='0'; _offset++; }
        }
        Digits(); if (_offset<_source.Length && _source[_offset]=='.') { _offset++; Digits(); }
        if (!digits) throw Error("Expected digits");
        if (_offset<_source.Length && _source[_offset] is 'e' or 'E')
        {
            _offset++; if (_offset<_source.Length && _source[_offset] is '+' or '-') _offset++;
            var exponentStart=_offset;
            while (_offset<_source.Length && char.IsAsciiDigit(_source[_offset])) _offset++;
            if (exponentStart==_offset) throw Error("Expected exponent digits");
        }
        if (!double.TryParse(_source.AsSpan(start,_offset-start),NumberStyles.Float,CultureInfo.InvariantCulture,out var number) ||
            !double.IsFinite(number) || number == 0 && nonzero) throw Error("Number outside supported finite binary64 range");
        return N("number",number:number);
    }
}
