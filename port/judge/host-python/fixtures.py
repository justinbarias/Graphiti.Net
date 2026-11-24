"""Custom entity and edge types for flow cases. Cases refer to them with {"$ref": "fixtures:Name"}.

The .NET host needs the same types with the same field names, descriptions, and docstrings,
because all of them reach prompt text and schema text.
"""
from pydantic import BaseModel, Field


class Person(BaseModel):
    """A human being who is named in the text."""

    occupation: str | None = Field(default=None, description='The job or profession of the person')
    home_city: str | None = Field(default=None, description='The city where the person lives')


class Product(BaseModel):
    """A commercial product or a product line that can be bought."""

    brand: str | None = Field(default=None, description='The brand that makes the product')
    category: str | None = Field(default=None, description='The kind of product, for example shoes')


class Company(BaseModel):
    """A business organization."""

    industry: str | None = Field(default=None, description='The industry of the company')


class Likes(BaseModel):
    """A person likes or prefers a product."""

    strength: str | None = Field(default=None, description='How strong the preference is: weak, medium or strong')


class WorksAt(BaseModel):
    """A person is employed by a company."""

    role: str | None = Field(default=None, description='The job title of the person at the company')
