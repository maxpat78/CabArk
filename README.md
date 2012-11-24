CabArk
======

This is a C# utility to create and extract archives in Microsoft Cabinet (CAB) format.

Currently, it provides only Deflate ("MS"-ZIP) compression via ZLIB library and can
handle single cabinets only.

There is an LZX interface for the library found at https://github.com/coderforlife/ms-compress,
but actually LZX-CAB support in such library does not work.

Take also a look at my PyCabArc utility: it is written in Python, it lacks an extractor, but
provides spanning cabinets sets, too!

In Wildcard.cs there is a C# implementation of the Win32 wildcard matching algorithm found
in my w32_fnmatch project.

