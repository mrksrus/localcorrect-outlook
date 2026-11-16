using System.Runtime.InteropServices;
// Only AddIn (and the Office interfaces it implements) should be COM-visible.
// The engine's public types stay out of the coclass map mscoree builds while activating.
[assembly: ComVisible(false)]
