<?xml version="1.0"?>
<!-- operates on the canonical JSON->XML mapping of tiny.json -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/">
    <table>
      <xsl:for-each select="root/entries/item">
        <row>
          <cell name="id"><xsl:value-of select="id"/></cell>
          <cell name="value"><xsl:value-of select="value"/></cell>
        </row>
      </xsl:for-each>
    </table>
  </xsl:template>
</xsl:stylesheet>
