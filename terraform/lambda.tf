resource "aws_security_group" "lambda" {
  name        = "${var.name_prefix}-lambda"
  description = "Egress-only: the scraper calls HTTPS out and accepts nothing in"
  vpc_id      = aws_vpc.main.id

  egress {
    description = "HTTPS to ENTSO-E (via fck-nat) and S3 (via gateway endpoint)"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

# Explicit log group with retention — the auto-created one never expires.
resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${var.name_prefix}"
  retention_in_days = 14
}

resource "aws_lambda_function" "scraper" {
  function_name = var.name_prefix
  description   = "Universal declarative API scraper: fetch -> XSLT -> XSD gate -> CSV in S3"

  filename         = var.lambda_zip
  source_code_hash = filebase64sha256(var.lambda_zip)

  runtime       = "python3.13"
  architectures = ["arm64"]
  handler       = "powerex_scraper.handler.lambda_handler"
  memory_size   = 512
  # Endpoints run sequentially; each fetch is bounded by 3 attempts x 30s
  # plus backoff (~98s worst case), so 300s covers the shipped config with
  # headroom for growth.
  timeout = 300

  role = aws_iam_role.lambda.arn

  vpc_config {
    subnet_ids         = [aws_subnet.private.id]
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      OUTPUT_BUCKET = aws_s3_bucket.data.bucket
      API_KEY       = var.api_key
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.lambda,
    aws_iam_role_policy_attachment.lambda_vpc,
  ]
}
