#!/bin/bash

echo "🚀 Starting A3ITranslator API..."
az container start --name a3itranslator-api --resource-group A3ITranslationRG

echo "⏳ Waiting for container to start..."
sleep 30

echo "📊 Checking container status..."
az container show --name a3itranslator-api --resource-group A3ITranslationRG --query "instanceView.state" --output tsv

echo "🌐 API URL: http://a3itranslator-api.northeurope.azurecontainer.io:8000"
echo "📋 Health Check: http://a3itranslator-api.northeurope.azurecontainer.io:8000/health"
echo "📖 Swagger UI: http://a3itranslator-api.northeurope.azurecontainer.io:8000/swagger"
