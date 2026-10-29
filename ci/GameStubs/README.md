# Game stand-ins (generated)

Written by `tools/StubGen` from what the plugin references. Each folder is an assembly with the game's real assembly name,
holding only the types and members the plugin uses, with their real names and signatures and no bodies. CI compiles the
plugin against them because the game's own assemblies cannot be published; the compiled DLL then binds to the real ones
inside the game. They are never shipped.

Regenerate (needs the game's interop folder) with `tools/regenerate-stubs.sh`, which also checks that the stand-in build and
the real build reference exactly the same members. Do not edit by hand.
