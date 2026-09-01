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
  description   = "Scrapes ENTSO-E Transparency Platform datasets to S3 as CSV (config-driven)"

  filename         = var.lambda_zip
  source_code_hash = filebase64sha256(var.lambda_zip)

  runtime       = "dotnet10"
  architectures = ["arm64"]
  handler       = "PowerexScraper::PowerexScraper.Function::HandleAsync"
  memory_size   = 512
  # Per-dataset HTTP pipeline is capped at 90s; a run-all invocation is N x 90s (datasets are
  # processed sequentially, one per try/catch) plus S3 puts. 300s gives headroom for the
  # 2-dataset default and room to grow before this needs revisiting.
  timeout = 300

  role = aws_iam_role.lambda.arn

  vpc_config {
    subnet_ids         = [aws_subnet.private.id]
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      OUTPUT_BUCKET   = aws_s3_bucket.data.bucket
      ENTSOE_BASE_URL = var.entsoe_base_url
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.lambda,
    aws_iam_role_policy_attachment.lambda_vpc,
  ]
}
