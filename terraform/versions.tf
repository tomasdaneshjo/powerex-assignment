terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # Solo operator + ephemeral stack ⇒ local state. The moment a second operator
  # or CI exists, move state to S3 + lockfile:
  # backend "s3" {
  #   bucket       = "<state-bucket>"
  #   key          = "powerex-scraper/terraform.tfstate"
  #   region       = "eu-central-1"
  #   use_lockfile = true
  # }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      project    = "powerex-entsoe-scraper"
      managed-by = "terraform"
    }
  }
}
