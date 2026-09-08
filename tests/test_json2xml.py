from lxml import etree

from powerex_scraper.json2xml import json_to_xml


def render(doc) -> str:
    return etree.tostring(json_to_xml(doc)).decode()


def test_ncname_keys_become_elements():
    assert render({"area": "SK"}) == "<root><area>SK</area></root>"


def test_non_ncname_keys_become_item_with_key_attribute():
    xml = render({"0": "x", "a b": "y", "a.b": "z"})
    assert '<item key="0">x</item>' in xml       # digit-leading
    assert '<item key="a b">y</item>' in xml     # space
    assert "<a.b>z</a.b>" in xml                 # dots are valid XML name chars


def test_arrays_become_indexed_items():
    assert render({"vals": [1.5, "a"]}) == \
        '<root><vals><item index="0">1.5</item><item index="1">a</item></vals></root>'


def test_null_and_booleans():
    xml = render({"n": None, "t": True, "f": False})
    assert '<n nil="true"/>' in xml
    assert "<t>true</t>" in xml and "<f>false</f>" in xml


def test_nested_structure_round_trips_the_documented_example():
    xml = render({"pointMap": {"0": [2780.4, None]}})
    assert xml == ('<root><pointMap><item key="0">'
                   '<item index="0">2780.4</item><item index="1" nil="true"/>'
                   "</item></pointMap></root>")


def test_keys_shadowing_xml_reserved_prefix_are_wrapped():
    assert '<item key="xmlns">v</item>' in render({"xmlns": "v"})
