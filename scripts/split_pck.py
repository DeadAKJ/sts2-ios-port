import os
import sys

CHUNK_SIZE = 90 * 1024 * 1024  # 90 MB (well under GitHub's 100MB limit)

def split_pck(input_path, output_dir):
    if not os.path.exists(input_path):
        print(f"Error: {input_path} not found.")
        sys.exit(1)
        
    os.makedirs(output_dir, exist_ok=True)
    file_size = os.path.getsize(input_path)
    print(f"Splitting {input_path} ({file_size / (1024*1024):.1f} MB) into 90MB parts...")
    
    part_num = 0
    with open(input_path, "rb") as f:
        while True:
            chunk = f.read(CHUNK_SIZE)
            if not chunk:
                break
            part_name = f"SlayTheSpire2.pck.part_{part_num:03d}"
            part_path = os.path.join(output_dir, part_name)
            with open(part_path, "wb") as part_f:
                part_f.write(chunk)
            print(f"  Created {part_name} ({len(chunk) / (1024*1024):.1f} MB)")
            part_num += 1

    print(f"Done! Created {part_num} parts in {output_dir}.")
    print("These can be safely pushed to GitHub without hitting the 100MB file limit.")

if __name__ == "__main__":
    src = r"E:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck"
    dst = os.path.join(os.path.dirname(__file__), "..", "pck_parts")
    split_pck(src, dst)
