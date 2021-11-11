# LineCounter

[![Build Status](https://github.com/mikefourie/LineCounter/workflows/.NET/badge.svg)](https://github.com/mikefourie/LineCounter/actions)


Count all lines under a given path. Categorization by file type. This is a useful tool for finding what languages are in a code base and how much code there is.

Results can be exported to CSV.

### Example

```
PS C:\gitcode\LineCounter\bin\Release\net6.0> .\LineCounter.exe --path C:\myrepo\
Line Counter --- run dotnet linecounter.dll --help for help
----------------------------------------------------------------------

Scanning: C:\myrepo\
Category                  Files  Lines    Code     Comments  Empty   Files Inc.  Files Excl.
--------------------------------------------------------------------------------------------
Batch                     120    3846     3140     10        696     120         0
BUILD-Extensionless       474    49160    44650    2         4508    474         0
CMake                     21     2903     1660     822       421     21          0
C++                       7188   1172126  874066   138815    159245  7188        0
Dockerfile-Extensionless  8      230      182      0         48      8           0
Go                        5      627      527      27        73      5           0
Git                       75     734      613      46        75      75          0
Ini                       54     1608     1051     51        506     54          0
Java                      26     2800     2139     242       419     26          0
Json                      75     4762     4745     0         17      75          0
Markdown                  358    24179    18275    0         5904    358         0
Projects                  10     2038     2038     0         0       10          0
Python                    1651   251801   196334   10141     45326   1651        0
Text                      190    124927   121265   0         3662    190         0
Web                       11     2080     1708     6         366     11          0
XML                       283    39241    34520    3757      964     283         0
YAML                      994    3361632  3358192  0         3440    994         0
TOTAL                     11543  5044694  4665105  153919    225670  11543       0

Scan Time: 1s:430ms
```
