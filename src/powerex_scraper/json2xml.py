"""Canonical, deterministic JSON→XML conversion.

XSLT stylesheets are written against this mapping, so it is a documented
contract:

    - the document root is <root>
    - an object member whose key is a valid XML name becomes an element of
      that name (dots and hyphens are legal); any other key (leading
      digits, spaces, reserved prefixes …) becomes
      <item key="ORIGINAL">
    - every array element becomes <item index="N"> (0-based)
    - scalars become text content; null becomes an empty element carrying
      nil="true"; booleans render as "true"/"false"; numbers via str()

Example — {"pointMap": {"0": [2780.4, null]}} converts to:

    <root>
      <pointMap>
        <item key="0">
          <item index="0">2780.4</item>
          <item index="1" nil="true"/>
        </item>
      </pointMap>
    </root>
"""
from __future__ import annotations

import re

from lxml import etree

_NCNAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_.-]*$")


def _append(parent: etree._Element, key: str | None, value, index: int | None = None):
    if key is not None and _NCNAME.match(key) and not key.lower().startswith("xml"):
        node = etree.SubElement(parent, key)
    else:
        node = etree.SubElement(parent, "item")
        if key is not None:
            node.set("key", key)
    if index is not None:
        node.set("index", str(index))

    if isinstance(value, dict):
        for child_key, child_value in value.items():
            _append(node, child_key, child_value)
    elif isinstance(value, list):
        for i, element in enumerate(value):
            _append(node, None, element, index=i)
    elif value is None:
        node.set("nil", "true")
    elif isinstance(value, bool):
        node.text = "true" if value else "false"
    else:
        node.text = str(value)
    return node


def json_to_xml(doc) -> etree._Element:
    root = etree.Element("root")
    if isinstance(doc, dict):
        for key, value in doc.items():
            _append(root, key, value)
    elif isinstance(doc, list):
        for i, element in enumerate(doc):
            _append(root, None, element, index=i)
    elif doc is None:
        root.set("nil", "true")
    else:
        root.text = str(doc) if not isinstance(doc, bool) else ("true" if doc else "false")
    return root
