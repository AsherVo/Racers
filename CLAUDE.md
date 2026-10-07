# Coding Standards
All code should be self-documenting.

## Engine Code
Engine code should be rock solid.

## Tool Code
Tool code should be of average quality.

## Game Code
Game code is dynamic, changing, and often uses non-ideal coding practices. This is fine. Do NOT add any comments to game code unless directly instructed.

## Naming
- Types, functions, methods, and enum values: PascalCase (`YamlNode`, `ViewFit`, `YamlMap`).
- Variables, parameters and struct fields: camelCase (`fbWidth`, `pixelPerfect`).
- Constants: SCREAMING_CASE (`CLEAR_COLOR`).

## Formatting (C#)
- Calls: spaces inside the parens, none before: `FooBar( x, y )`. Empty: `FooBar()`.
- Generics: spaces inside the carrots: `FooBar< T >()`.
- Declarations: a space before the parens and inside them: `bool FooBar ( i32 x, i32 y )`. Empty: `i32 main ()`.
- Parens that group an expression get the same treatment: `( a + b ) * c`.
- Braces go on their own line (Allman) for functions, structs, `if`/`else`/`for`/`while`/`switch`/`case`. `else` starts its own line after `}`.
- Exception: a control-flow block that fits on one line does not need paretheses

```C#
if ( condition )
    return;```