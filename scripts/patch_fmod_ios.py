import struct
import glob
import os

def patch_fmod():
    pattern = 'godot/addons/fmod/**/libGodotFmod.ios.template_release.universal.dylib'
    files = glob.glob(pattern, recursive=True)
    if not files:
        print(f"Warning: No files found matching {pattern}")
        return
    
    for path in files:
        with open(path, 'r+b') as f:
            f.seek(119508)
            orig = struct.unpack('<I', f.read(4))[0]
            print(f"File {path}: opcode at 119508 is 0x{orig:08x}")
            if orig == 0x940b2d0e:
                f.seek(119508)
                # Patch BL _load_all_fmod_plugins -> B 0x1d30c (+56 bytes = 14 instructions)
                f.write(struct.pack('<I', 0x1400000e))
                print(f"Successfully patched {path} to bypass load_all_fmod_plugins")
            elif orig == 0x1400000e:
                print(f"{path} is already patched")
            else:
                print(f"Warning: Unexpected opcode 0x{orig:08x} at 119508 in {path}")

if __name__ == '__main__':
    patch_fmod()
