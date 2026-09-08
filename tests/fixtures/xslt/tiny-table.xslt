<?xml version="1.0"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/">
    <table>
      <xsl:for-each select="//entry">
        <row>
          <cell name="id"><xsl:value-of select="@id"/></cell>
          <cell name="value"><xsl:value-of select="."/></cell>
        </row>
      </xsl:for-each>
    </table>
  </xsl:template>
</xsl:stylesheet>
