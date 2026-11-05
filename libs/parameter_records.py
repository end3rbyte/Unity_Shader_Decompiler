# Reads versioned Unity shader parameter records without assigning bindings.
"""Parse the parameter records referenced by serialized D3D11 programs."""

import struct


# Reads bounds-checked fields from one parameter record.
class ParameterReader:
    # Starts at the beginning of a record.
    def __init__(self, data):
        self.data = data
        self.position = 0

    # Reads one signed 32-bit field.
    def integer(self):
        if self.position + 4 > len(self.data):
            raise ValueError("Truncated shader parameter record")
        value = struct.unpack_from("<i", self.data, self.position)[0]
        self.position += 4
        return value

    # Reads a bounded array count.
    def count(self):
        value = self.integer()
        if value < 0 or value > (len(self.data) - self.position) // 4:
            raise ValueError(f"Invalid shader parameter count: {value}")
        return value

    # Reads a padded UTF-8 identifier.
    def string(self):
        length = self.integer()
        end = self.position + length
        if length < 0 or end > len(self.data):
            raise ValueError("Invalid shader parameter name length")
        value = self.data[self.position:end].decode("utf-8")
        self.position = (end + 3) & ~3
        if self.position > len(self.data):
            raise ValueError("Truncated shader parameter name padding")
        return value


# Reads a vector or matrix member with its serialized layout.
def read_variable(reader):
    name = reader.string()
    kind, rows, columns, matrix, array_size, offset = (reader.integer() for _ in range(6))
    if not name or kind not in range(6) or rows not in range(1, 5) or columns not in range(1, 5):
        raise ValueError(f"Unsupported shader parameter shape: {name!r}")
    if offset < 0 or offset % 4 or array_size < 0 or matrix not in (0, 1):
        raise ValueError(f"Invalid shader parameter layout: {name}")
    if not matrix and rows != 1:
        raise ValueError(f"Unsupported nonmatrix parameter rows: {name}")
    return {"name": name, "type": kind, "rows": rows, "columns": columns,
            "isMatrix": bool(matrix), "arraySize": array_size, "offset": offset}


# Reads a serialized aggregate and all of its members.
def read_structure(reader):
    name = reader.string()
    offset, array_size, size = (reader.integer() for _ in range(3))
    if not name or offset < 0 or offset % 4 or array_size < 0 or size < 0:
        raise ValueError(f"Invalid shader structure layout: {name!r}")
    members = [read_variable(reader) for _ in range(reader.count())]
    return {"name": name, "offset": offset, "arraySize": array_size,
            "size": size, "members": members}


# Reads a named constant buffer and its complete member lists.
def read_buffer(reader):
    name, size = reader.string(), reader.integer()
    if size < 0:
        raise ValueError(f"Negative constant-buffer size: {name}")
    vectors = [read_variable(reader) for _ in range(reader.count())]
    structures = [read_structure(reader) for _ in range(reader.count())]
    if not name and (size or vectors or structures):
        raise ValueError("Unbound global constants in shader parameter record")
    members = vectors + structures
    if any(item["offset"] >= size for item in members):
        raise ValueError(f"Constant-buffer member exceeds {name} size")
    if len({item["name"] for item in members}) != len(members):
        raise ValueError(f"Duplicate constant-buffer member name: {name}")
    return {"name": name, "size": size, "variables": vectors, "structures": structures}


# Reads one resource and preserves its native numeric fields.
def read_resource(reader):
    name, kind = reader.string(), reader.integer()
    if not name or kind not in range(5):
        raise ValueError(f"Unsupported shader resource type: {name!r} ({kind})")
    fields = [reader.integer() for _ in range(3 if kind == 0 else 2)]
    if kind == 0:
        if fields[0] < 0 or fields[1] < -1 or fields[2] < 0:
            raise ValueError(f"Invalid texture resource fields: {name}")
        return {"name": name, "kind": kind, "slot": fields[0],
                "samplerIndex": fields[1], "dimensionFlags": fields[2]}
    if fields[0] < 0:
        raise ValueError(f"Negative shader resource slot: {name}")
    return {"name": name, "kind": kind, "slot": fields[0], "extra": fields[1]}


# Parses one complete binary parameter record.
def read_parameters(data):
    reader = ParameterReader(data)
    version = reader.integer()
    if version != 202012090:
        raise ValueError(f"Unsupported shader parameter record version: {version}")
    buffers = [read_buffer(reader) for _ in range(reader.count())]
    resources = [read_resource(reader) for _ in range(reader.count())]
    if reader.position != len(data):
        raise ValueError(f"Unread shader parameter bytes: {len(data) - reader.position}")
    if len({item['name'] for item in buffers}) != len(buffers):
        raise ValueError("Duplicate constant-buffer name in parameter record")
    if len({(item['kind'], item['slot']) for item in resources}) != len(resources):
        raise ValueError("Duplicate resource slot in parameter record")
    return {"version": version, "constantBuffers": buffers, "resources": resources}
