using System;
using System.Collections.Generic;

namespace CabArk
{
    //
    // Win32 CMD command prompt (NT 3.1+) wildcards matching algorithm,
    // implementing the following rules (when the file system supports long names):
    //
    //   1. * and *.* match all
    //   2. *. matches all without extension
    //   3. .* repeated n times matches without or with up to n extensions
    //   4. ? matches 1 character; 0 or 1 if followed by only wildcards
    //   5. * matches multiple dots; ? does not (except in NT 3.1)
    //   6. *.xyz (3 characters ext, even with 1-2 ??) matches any longer xyz ext
    //   7. [ and ] are valid name characters
    //
    // According to official sources, the star should match zero or more characters,
    // and the question mark exactly one.
    //
    // Reviewing the help for FsRtlIsNameInExpression API in NT kernel, it seems
    // easy to say that the command interpretr implements a mix of rules. MSDN says:
    //
    // * (asterisk)            Matches zero or more characters.
    // ? (question mark)       Matches a single character.
    // DOS_DOT                 Matches either a period or zero characters beyond the name string.
    // DOS_QM                  Matches any single character or, upon encountering a period or end of name string,
    //                         advances the expression to the end of the set of contiguous DOS_QMs.
    // DOS_STAR                Matches zero or more characters until encountering and matching the final . in the
    //                         name.
    //
    // In the COMMAND.COM rules are different:
    //
    //   1. * matches all file names without extension
    //   2. .* matches all extensions
    //   3. characters after a star are discarded
    //   4. [ and ] aren't valid in file names
    //   5. ? follows CMD's rule 4
    //   6. neither ? nor * matches multiple dots
    //
    // Under Windows 9x/ME, COMMAND.COM has long file names support and follows
    // rules 1-2 and 5-7 like CMD; but ? matches 1 character only, except dot.
    //
    public class Win32Wildcards
    {
#if MAIN
        static public void Main()
        {
            Tuple<string, string, bool>[] cases = 
            {
                Tuple.Create("ab[1].c", "ab[1].c", true),                   // Win32 must match, [] aren't wildcards
                Tuple.Create("abc.d", "AbC.d", true),                       // Win32 must match, file system is case-insensitive
                Tuple.Create("ab", "ab?", true),                            // 0|1 char
                Tuple.Create("ac", "a?c", false),                           // 1 char
                Tuple.Create("abc", "a??c", false),                         // 2 chars
                Tuple.Create("abcd", "a??c", false),                        // 2 chars
                Tuple.Create("abcc", "a??c", true),                         // 2 chars
                Tuple.Create("abc", "*.", true),                            // no ext
                Tuple.Create("abc.d", "*.", false),
                Tuple.Create("abc.d", "*.*d", true),                        // ext ending in "d"
                Tuple.Create("ab.cd", "*.*d", true),
                Tuple.Create("abc", "*.*", true),                           // with ext or not
                Tuple.Create("abc.d", "*.*", true),
                Tuple.Create("abc", "*ab.*", false),
                Tuple.Create("abc", "*abc.*", true),
                Tuple.Create("abc", "*.?", true),
                Tuple.Create("abc.d", "*.*", true),
                Tuple.Create("abc.d", "*.?", true),
                Tuple.Create("ab", "a????", true),                          // a + 0-4 chars
                Tuple.Create("abcde", "a????", true),                       // a + 0-4 chars
                Tuple.Create("ab", "a????.??", true),                       // a + 0-4 chars, w/ or w/o ext of 1-2 chars
                Tuple.Create("ab", "?a????", false),
                Tuple.Create("ab.c", "a????.??", true),
                Tuple.Create("ab.cd", "a????.??", true),
                Tuple.Create("ab.cde", "a????.??", false),
                Tuple.Create("ab.c", "ab.?", true),                         // w/o ext or w/ 1 char ext
                Tuple.Create("abc", "ab.?", false),
                Tuple.Create("ab", "ab.?", true),
                Tuple.Create("ab.ca", "ab.?a", true),                       // w/ 2 chars ext ending in a
                Tuple.Create("ab", "ab.?a", false),
                Tuple.Create("ab.ca", "ab.*", true),                        // any ext
                Tuple.Create("b...txt", "b*.txt", true),                    // b with anything ending in .txt
                Tuple.Create("b...txt", "b??.txt", false),                  // it seems logic, but doesn't work at the Prompt!
                Tuple.Create("b....txt", "b...txt", false),
                Tuple.Create("minilj.txt", "*.ini", false),
                Tuple.Create("abcde.fgh", "abc*.", false),
                Tuple.Create("abcde", "abc*.", true),
                Tuple.Create("abcde", "ab*e", true),
                Tuple.Create("abc", "ab*e", false),
                Tuple.Create("abc", "abc.*", true),
                Tuple.Create("abc.de.fgh", "abc.*", true),
                Tuple.Create("abc.de.fgh", "abc.*.*", true),
                Tuple.Create("abc.de.fgh", "abc.??.*", true),
                Tuple.Create("abc.fgh", "abc.*.*", true),
                Tuple.Create("abc.fgh", "abc.*.", true),
                Tuple.Create("abc.fgh", "abc.*..", true),
                Tuple.Create("abcfgh", "abc.*.*", false),
                Tuple.Create("abc.de.fgh", "*.de.f*", true),
                Tuple.Create("abc.de.fgh", "*de.f*", true),
                Tuple.Create("abc.de.fgh", "*f*", true),
                Tuple.Create("abc..de...fgh", "*de*f*", true),
                Tuple.Create("abc..de...fgh", "abc..de.*fgh", true),
                Tuple.Create("abc.d", "***?*", true),
                Tuple.Create("abc.d.e", "*.e", true),                       // with ending .e ext
                Tuple.Create("abc.e.ef", "*.e", false),
                Tuple.Create("abc.e.e", "*.e", true),
                Tuple.Create("abc.e.ef", "*.e*", true),                     // with .e ext
                Tuple.Create("abc.e.e", "*.e*", true),
                Tuple.Create("abc.e.effe", "*.e*e", true),
                Tuple.Create("abcde.fgh", "*.fgh", true),
                Tuple.Create("abcde.fghi", "*.fgh", true),                  // Prompt says TRUE!!!
                Tuple.Create("abcde.fghi", "*.fg?", true),                  // And so here!
                Tuple.Create("abcde.fghi", "*.?gh", true),                  // And so here!
                Tuple.Create("abcde.fghi", "*.f??", true),                  // And so here!
                Tuple.Create("abcde.fghil", "abc??*.fgh", true),            // And so here!
                Tuple.Create("abcde.fghi", "abc??.fgh", false),             // Here Prompt works!!!
                Tuple.Create("abcde.fghil", "*.fghi", false),               // Here too...
                Tuple.Create("abcde.fgh.fgh", "*.fgh", true),
                Tuple.Create("abcde.fgh.fg", "*.fgh", false),
                Tuple.Create("abcde.fg.fgh", "*.fgh", true),
                Tuple.Create("abcde.fghabc.fghab", "*.fgh", true),          // And here!
                Tuple.Create("abcde.fg.fgh.fgho", "*.fghi", false),
                Tuple.Create("abcde.fg.fgh.fgho", "*.fgh?", true),
            };

            int failed = 0;

            foreach (Tuple<string, string, bool> t in cases)
            {
                bool r = Match(t.Item1, t.Item2);
                if (r != t.Item3)
                {
			        failed++;
			        Console.WriteLine("'{0}' ~= '{1}' is {2}, expected {3}", t.Item2, t.Item1, r, t.Item3);
        		}
            }

            if (failed > 0)
                Console.WriteLine("{0} tests failed.", failed);
            else
                Console.WriteLine("All {0} tests passed!", cases.Length);

            Console.ReadLine();
        }
#endif
        public static bool Match(string s, string pattern, bool bCaseInsensitive=true)
        {
            string p = pattern;
            int si = 0, pi = 0;
            int ls = s.Length;
            int lp = pattern.Length;
            Stack<Tuple<int, int>> p_anchors = new Stack<Tuple<int, int>>();
            Tuple<int, int> t;

            if (bCaseInsensitive)
            {
                s = s.ToLower();
                p = pattern.ToLower();
            }

            while (true)
            {
                while (si < ls && pi < lp)
                {
                    if (p[pi] == s[si])
                    {
                        // Dot matched not at EOS
                        if (pi + 1 == lp && p[pi] == '.' && si < ls)
                            break;
                        pi++;
                        si++;
                    }
                    else if (p[pi] == '?')
                    {
                        // Dot force to skip a ? sequence
                        if (s[si] == '.')
                            while (pi < lp && p[pi] == '?') pi++;
                        pi++;
                        si++;
                    }
                    else if (p[pi] == '*')
                    {
                        // Record star position to eventually restart search
                        int y = pi;
                        // Skips multiple stars
                        while (pi < lp && p[pi] == '*') pi++;
                        // Ending star matches all
                        if (pi == lp)
                        {
                            si = ls;
                            break;
                        }
                        // Star superseeds question mark
                        while (pi < lp && p[pi] == '?') pi++;
                        // Record match start
                        p_anchors.Push(new Tuple<int, int>(y, si));
                        // Star matches until subexpression is matched
                        while (si < ls && s[si] != p[pi]) si++;
                    }
                    else if (p_anchors.Count > 0)
                    {
                        // Restarts search from star after last match in string
                        t = p_anchors.Pop();
                        pi = t.Item1;
                        si = t.Item2;
                        si++;
                    }
                    else
                        break;
                }

                // Repeats loop until other searches are possible
                if (si >= ls || p_anchors.Count == 0)
                    break;

                // Dot matched not at EOS
                if (pi + 1 == lp && p[pi] == '.')
                    return false;

                // Exception: *.xyz matches extensions beginning with .xyz, even with 1 or 2 ?
                if (lp >= 5 && ls - si < 3 && p[p.Length-5] == '*' && p[p.Length-4] == '.' && s[si - 4] == '.')
                    return true;

                t = p_anchors.Pop();
                pi = t.Item1;
                si = t.Item2;
                si++;
                continue;
            }

            while (pi < lp && (p[pi] == '*' || p[pi] == '?' || p[pi] == '.')) pi++;

            if (si == ls && pi < lp && p[pi] == '.')
            {
                // Dot matches EOS only
                if (pi+1 == lp)
                    pi++;
                else
                {
                    pi++;
                    while (pi < lp && (p[pi] == '*' || p[pi] == '?')) pi++;
                }
            }

            // String and pattern both consumed, matches!
            if (pi == lp && si == ls)
                return true;
            else
                return false;
        }
    }
}
