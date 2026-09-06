output "bucket_name" {
  description = "S3 bucket receiving the CSVs"
  value       = aws_s3_bucket.data.bucket
}

output "function_name" {
  description = "Lambda function name (for aws lambda invoke)"
  value       = aws_lambda_function.scraper.function_name
}

# Verified against RaJiska/fck-nat/aws v1.6.1 (.terraform/modules/fck_nat/output.tf):
# instance_public_ip exists and returns aws_instance.main[0].public_ip when ha_mode
# is false (our case). Null in HA mode, which we don't use.
output "nat_public_ip" {
  description = "Public IP the scraper egresses from"
  value       = module.fck_nat.instance_public_ip
}
